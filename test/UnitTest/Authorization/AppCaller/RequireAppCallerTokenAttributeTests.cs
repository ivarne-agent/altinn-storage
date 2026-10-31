using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Authorization.AppCaller;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Authorization.AppCaller;

public sealed class RequireAppCallerTokenAttributeTests : IDisposable
{
    private const string SyncAdapterScope = "altinn:storage/instances.syncadapter";
    private const string Token = "header.payload.signature";

    private readonly Guid _instanceGuid = Guid.NewGuid();

    // Unique per test so measurements from tests running in parallel can be told apart.
    private readonly string _org = $"org-{Guid.NewGuid():N}";
    private readonly Mock<IAppCallerTokenValidator> _validator = new();
    private readonly Mock<IInstanceRepository> _instanceRepository = new();
    private readonly Mock<IAuthorization> _authorization = new();
    private readonly AppCallerSettings _settings = new();
    private readonly ConcurrentBag<Dictionary<string, object?>> _measurements = [];
    private readonly MeterListener _listener = new();

    public RequireAppCallerTokenAttributeTests()
    {
        _instanceRepository
            .Setup(r => r.GetOne(_instanceGuid, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new InstanceInternal
                {
                    Id = _instanceGuid,
                    AppId = $"{_org}/app",
                    Org = _org,
                }
            );

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "storage.app_caller.validations")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(
            (_, _, tags, _) =>
            {
                Dictionary<string, object?> recorded = new();
                foreach (KeyValuePair<string, object?> tag in tags)
                {
                    recorded[tag.Key] = tag.Value;
                }

                _measurements.Add(recorded);
            }
        );
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public async Task Off_DoesNothing()
    {
        _settings.Mode = AppCallerMode.Off;
        AuthorizationFilterContext context = CreateInstanceContext(Token);

        await new RequireAppCallerTokenAttribute().OnAuthorizationAsync(context);

        Assert.Null(context.Result);
        _validator.VerifyNoOtherCalls();
        _instanceRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Observe_InvalidToken_LetsRequestThroughAndRecords()
    {
        _settings.Mode = AppCallerMode.Observe;
        SetupValidator(new(AppCallerOutcome.Invalid, AppCallerReasons.Expired, _org, Kid: "v1"));
        AuthorizationFilterContext context = CreateInstanceContext(Token);

        await new RequireAppCallerTokenAttribute().OnAuthorizationAsync(context);

        Assert.Null(context.Result);
        _validator.Verify(v =>
            v.ValidateAsync(Token, $"{_org}/app", It.IsAny<CancellationToken>())
        );
        AssertRecorded("invalid", AppCallerReasons.Expired, "observe");
    }

    [Fact]
    public async Task Observe_MissingToken_RecordsOrgOfInstance()
    {
        _settings.Mode = AppCallerMode.Observe;
        SetupValidator(new(AppCallerOutcome.Missing, AppCallerReasons.Missing));
        AuthorizationFilterContext context = CreateInstanceContext(token: null);

        await new RequireAppCallerTokenAttribute().OnAuthorizationAsync(context);

        Assert.Null(context.Result);
        _validator.Verify(v =>
            v.ValidateAsync(
                It.Is<string?>(t => string.IsNullOrEmpty(t)),
                $"{_org}/app",
                It.IsAny<CancellationToken>()
            )
        );
        AssertRecorded("missing", AppCallerReasons.Missing, "observe");
    }

    [Fact]
    public async Task Enforce_ValidToken_LetsRequestThrough()
    {
        _settings.Mode = AppCallerMode.Enforce;
        SetupValidator(new(AppCallerOutcome.Valid, AppCallerReasons.Ok, _org, $"{_org}/app", "v1"));
        AuthorizationFilterContext context = CreateInstanceContext(Token);

        await new RequireAppCallerTokenAttribute().OnAuthorizationAsync(context);

        Assert.Null(context.Result);
        AssertRecorded("valid", AppCallerReasons.Ok, "enforce");
    }

    [Theory]
    [InlineData(AppCallerOutcome.Missing, AppCallerReasons.Missing)]
    [InlineData(AppCallerOutcome.Invalid, AppCallerReasons.UnknownKid)]
    public async Task Enforce_MissingOrInvalidToken_Returns401ProblemDetails(
        AppCallerOutcome outcome,
        string reason
    )
    {
        _settings.Mode = AppCallerMode.Enforce;
        SetupValidator(new(outcome, reason));
        AuthorizationFilterContext context = CreateInstanceContext(Token);

        await new RequireAppCallerTokenAttribute().OnAuthorizationAsync(context);

        ObjectResult result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.Status);
        Assert.Contains(reason, problem.Detail);
        Assert.Contains("application/problem+json", result.ContentTypes);
    }

    [Fact]
    public async Task Enforce_TokenForAnotherApp_Returns403ProblemDetails()
    {
        _settings.Mode = AppCallerMode.Enforce;
        SetupValidator(
            new(AppCallerOutcome.WrongApp, AppCallerReasons.WrongApp, _org, $"{_org}/other", "v1")
        );
        AuthorizationFilterContext context = CreateInstanceContext(Token);

        await new RequireAppCallerTokenAttribute().OnAuthorizationAsync(context);

        ObjectResult result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
        AssertRecorded("wrong_app", AppCallerReasons.WrongApp, "enforce");
    }

    [Fact]
    public async Task Enforce_InstanceNotFound_LetsActionReturnItsOwn404()
    {
        _settings.Mode = AppCallerMode.Enforce;
        AuthorizationFilterContext context = CreateInstanceContext(Token, Guid.NewGuid());

        await new RequireAppCallerTokenAttribute().OnAuthorizationAsync(context);

        Assert.Null(context.Result);
        _validator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Enforce_SyncAdapter_IsExempt()
    {
        _settings.Mode = AppCallerMode.Enforce;
        _authorization.Setup(a => a.UserHasRequiredScope(SyncAdapterScope)).Returns(true);
        AuthorizationFilterContext context = CreateInstanceContext(token: null);

        await new RequireAppCallerTokenAttribute().OnAuthorizationAsync(context);

        Assert.Null(context.Result);
        _validator.VerifyNoOtherCalls();
        _instanceRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Enforce_OrgAndAppRoute_UsesRouteWithoutLoadingInstance()
    {
        _settings.Mode = AppCallerMode.Enforce;
        SetupValidator(new(AppCallerOutcome.Valid, AppCallerReasons.Ok, _org, "ttd/app", "v1"));
        AuthorizationFilterContext context = CreateContext(
            Token,
            new RouteValueDictionary { ["org"] = "ttd", ["app"] = "app" }
        );

        await new RequireAppCallerTokenAttribute().OnAuthorizationAsync(context);

        Assert.Null(context.Result);
        _validator.Verify(v => v.ValidateAsync(Token, "ttd/app", It.IsAny<CancellationToken>()));
        _instanceRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Enforce_AppIdQuery_IsUsedWhenRouteHasNoTarget()
    {
        _settings.Mode = AppCallerMode.Enforce;
        SetupValidator(new(AppCallerOutcome.Valid, AppCallerReasons.Ok, _org, "ttd/app", "v1"));
        AuthorizationFilterContext context = CreateContext(Token, new RouteValueDictionary());
        context.HttpContext.Request.QueryString = new QueryString("?appId=ttd/app");

        await new RequireAppCallerTokenAttribute().OnAuthorizationAsync(context);

        Assert.Null(context.Result);
        _validator.Verify(v => v.ValidateAsync(Token, "ttd/app", It.IsAny<CancellationToken>()));
    }

    private void SetupValidator(AppCallerValidationResult result) =>
        _validator
            .Setup(v =>
                v.ValidateAsync(
                    It.IsAny<string?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(result);

    private void AssertRecorded(string outcome, string reason, string mode) =>
        Assert.Contains(
            _measurements,
            m =>
                Equals(m["org"], _org)
                && Equals(m["outcome"], outcome)
                && Equals(m["reason"], reason)
                && Equals(m["mode"], mode)
        );

    private AuthorizationFilterContext CreateInstanceContext(
        string? token,
        Guid? instanceGuid = null
    ) =>
        CreateContext(
            token,
            new RouteValueDictionary
            {
                ["instanceOwnerPartyId"] = "1337",
                ["instanceGuid"] = (instanceGuid ?? _instanceGuid).ToString(),
            }
        );

    private AuthorizationFilterContext CreateContext(
        string? token,
        RouteValueDictionary routeValues
    )
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(AppTokenTestHelper.Monitor(_settings));
        services.AddSingleton(
            Options.Create(new GeneralSettings { InstanceSyncAdapterScope = SyncAdapterScope })
        );
        services.AddSingleton(_validator.Object);
        services.AddSingleton(_instanceRepository.Object);
        services.AddSingleton(_authorization.Object);

        DefaultHttpContext httpContext = new()
        {
            RequestServices = services.BuildServiceProvider(),
        };
        if (token is not null)
        {
            httpContext.Request.Headers[RequireAppCallerTokenAttribute.HeaderName] = token;
        }

        RouteData routeData = new(routeValues);
        ActionContext actionContext = new(httpContext, routeData, new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }
}
