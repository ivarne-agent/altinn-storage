using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization.Scopes;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Authorization.Scopes;

public class InstanceScopeAuthorizerTests
{
    private const string AppWithScopes = "ttd/own-scopes";
    private const string AppWithoutScopes = "ttd/no-scopes";

    [Fact]
    public async Task AuthorizeInstance_EndpointWithoutScopeAttribute_FailsClosed()
    {
        (InstanceScopeAuthorizer sut, _) = Create(ScopeCheckMode.Observe, endpointMetadata: []);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.AuthorizeInstance(CreateInstance(AppWithoutScopes), CancellationToken.None)
        );
    }

    [Fact]
    public async Task AuthorizeInstance_FixedScopeEndpoint_IsLeftToTheFilter()
    {
        (InstanceScopeAuthorizer sut, HttpContext httpContext) = Create(
            ScopeCheckMode.Enforce,
            endpointMetadata:
            [
                new RequireScopeAttribute(ApiScopeRules.SyncAdapter, ScopeAccess.Read),
            ]
        );

        ProblemDetails? problem = await sut.AuthorizeInstance(
            CreateInstance(AppWithoutScopes),
            CancellationToken.None
        );

        Assert.Null(problem);
        Assert.Empty(httpContext.Features.Get<IHttpMetricsTagsFeature>()!.Tags);
    }

    [Theory]
    [InlineData(ScopeCheckMode.Enforce, true)]
    [InlineData(ScopeCheckMode.Observe, false)]
    [InlineData(ScopeCheckMode.Off, false)]
    public async Task AuthorizeInstance_MissingScope_RefusedOnlyWhenEnforced(
        ScopeCheckMode mode,
        bool refused
    )
    {
        (InstanceScopeAuthorizer sut, HttpContext httpContext) = Create(mode);

        ProblemDetails? problem = await sut.AuthorizeInstance(
            CreateInstance(AppWithoutScopes),
            CancellationToken.None
        );

        Assert.Equal(refused, problem is not null);
        if (refused)
        {
            Assert.Equal(StatusCodes.Status403Forbidden, problem!.Status);
            Assert.Equal("insufficient_scope", problem.Type);
        }

        List<KeyValuePair<string, object?>> tags = httpContext
            .Features.Get<IHttpMetricsTagsFeature>()!
            .Tags.ToList();
        if (mode == ScopeCheckMode.Off)
        {
            Assert.Empty(tags);
        }
        else
        {
            Assert.Contains(new("scope_check", "missing_builtin"), tags);
            Assert.Contains(new("scope_access", "read"), tags);
        }
    }

    [Fact]
    public async Task AuthorizeInstance_SeveralChecks_RecordsTheFirstDecisionOnly()
    {
        (InstanceScopeAuthorizer sut, HttpContext httpContext) = Create(
            ScopeCheckMode.Observe,
            scopes: ApiScopeRules.InstancesRead
        );

        await sut.AuthorizeInstance(CreateInstance(AppWithoutScopes), CancellationToken.None);
        await sut.AuthorizeInstance(CreateInstance(AppWithScopes), CancellationToken.None);

        List<KeyValuePair<string, object?>> tags = httpContext
            .Features.Get<IHttpMetricsTagsFeature>()!
            .Tags.ToList();
        Assert.Equal(2, tags.Count);
        Assert.Contains(new("scope_check", "ok"), tags);
    }

    [Theory]
    [InlineData(ScopeCheckMode.Enforce, 1)]
    [InlineData(ScopeCheckMode.Observe, 2)]
    [InlineData(ScopeCheckMode.Off, 2)]
    public async Task FilterByAppScopes_RemovesAppsWhoseOwnScopesTheTokenLacks(
        ScopeCheckMode mode,
        int expectedCount
    )
    {
        (InstanceScopeAuthorizer sut, _) = Create(mode, scopes: ApiScopeRules.InstancesRead);

        List<InstanceInternal> result = await sut.FilterByAppScopes(
            [CreateInstance(AppWithoutScopes), CreateInstance(AppWithScopes)],
            CancellationToken.None
        );

        Assert.Equal(expectedCount, result.Count);
        Assert.Contains(result, i => i.AppId == AppWithoutScopes);
    }

    [Theory]
    [InlineData(ScopeCheckMode.Enforce, false)]
    [InlineData(ScopeCheckMode.Observe, true)]
    [InlineData(ScopeCheckMode.Off, true)]
    public void CheckParty_OtherParty_TreatedAsMissingOnlyWhenEnforced(
        ScopeCheckMode mode,
        bool kept
    )
    {
        (InstanceScopeAuthorizer sut, _) = Create(mode);
        InstanceInternal instance = CreateInstance(AppWithoutScopes);

        Assert.Equal(kept, sut.CheckParty(instance, 2000) is not null);
        Assert.Same(instance, sut.CheckParty(instance, 1000));
    }

    private static (InstanceScopeAuthorizer Sut, HttpContext HttpContext) Create(
        ScopeCheckMode mode,
        string scopes = "altinn:something",
        object[]? endpointMetadata = null
    )
    {
        DefaultHttpContext httpContext = new()
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim("urn:altinn:userid", "1"), new Claim("scope", scopes)],
                    "test"
                )
            ),
        };
        httpContext.Features.Set<IHttpMetricsTagsFeature>(new MetricsTagsFeature());
        httpContext.SetEndpoint(
            new Endpoint(
                null,
                new EndpointMetadataCollection(
                    endpointMetadata ?? [new InstanceScopeAccessAttribute(ScopeAccess.Read)]
                ),
                "test endpoint"
            )
        );

        Mock<IApplicationRepository> applications = new();
        applications
            .Setup(r =>
                r.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(
                (string appId, string org, CancellationToken _) =>
                    new Application
                    {
                        Id = appId,
                        Org = org,
                        ApiScopes =
                            appId == AppWithScopes
                                ? new ApiScopesConfiguration
                                {
                                    Users = new ApiScopes { Read = "ttd:[app]/read" },
                                }
                                : null,
                    }
            );

        InstanceScopeAuthorizer sut = new(
            new HttpContextAccessor { HttpContext = httpContext },
            applications.Object,
            Options.Create(
                new GeneralSettings
                {
                    InstanceBuiltInScopeCheck = mode,
                    InstanceAppScopeCheck = mode,
                    InstanceSyncAdapterScope = ApiScopeRules.SyncAdapter,
                }
            ),
            NullLogger<InstanceScopeAuthorizer>.Instance
        );
        return (sut, httpContext);
    }

    private static InstanceInternal CreateInstance(string appId) =>
        new()
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            Org = "ttd",
            InstanceOwner = new InstanceOwner { PartyId = "1000" },
        };

    private sealed class MetricsTagsFeature : IHttpMetricsTagsFeature
    {
        public ICollection<KeyValuePair<string, object?>> Tags { get; } = [];

        public bool MetricsDisabled { get; set; }
    }
}
