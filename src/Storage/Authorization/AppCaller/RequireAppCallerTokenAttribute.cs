using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Altinn.Platform.Storage.Authorization.AppCaller;

/// <summary>
/// Requires the request to carry an <c>Altinn-App-Token</c> issued to the app that owns the target.
/// The target app is <c>{org}/{app}</c> from the route, the app of the instance identified by
/// <c>instanceGuid</c> in the route, or the <c>appId</c> query parameter, in that order.
/// </summary>
/// <remarks>
/// Runs as an authorization filter, after the endpoint's authorization policy and before model
/// binding. What happens on failure depends on <see cref="AppCallerSettings.Mode"/>.
/// Requests from the Dialogporten sync adapter are exempt: it is a platform caller with its own
/// scope, which the storage access handler already admits on these policies.
/// When the instance does not exist the check is skipped so the action returns its own 404.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireAppCallerTokenAttribute : Attribute, IAsyncAuthorizationFilter
{
    /// <summary>
    /// The header that carries the app token.
    /// </summary>
    public const string HeaderName = "Altinn-App-Token";

    /// <inheritdoc/>
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        IServiceProvider services = context.HttpContext.RequestServices;
        AppCallerMode mode = services
            .GetRequiredService<IOptionsMonitor<AppCallerSettings>>()
            .CurrentValue.Mode;

        if (mode == AppCallerMode.Off || context.Result is not null)
        {
            return;
        }

        string modeTag = mode == AppCallerMode.Enforce ? "enforce" : "observe";
        ILogger logger = services.GetRequiredService<ILogger<RequireAppCallerTokenAttribute>>();
        CancellationToken cancellationToken = context.HttpContext.RequestAborted;

        if (IsSyncAdapter(services))
        {
            AppCallerMetrics.RecordValidation("exempt", "sync_adapter", modeTag, null);
            return;
        }

        Target target = await ResolveTarget(context, services, cancellationToken);
        if (target.AppId is null)
        {
            AppCallerMetrics.RecordValidation(target.SkipReason, target.SkipReason, modeTag, null);
            logger.LogDebug(
                "App caller token check skipped for {Path}: {SkipReason}",
                context.HttpContext.Request.Path,
                target.SkipReason
            );
            return;
        }

        AppCallerValidationResult result = await services
            .GetRequiredService<IAppCallerTokenValidator>()
            .ValidateAsync(ReadToken(context.HttpContext), target.AppId, cancellationToken);

        AppCallerMetrics.RecordValidation(
            OutcomeTag(result.Outcome),
            result.Reason,
            modeTag,
            result.Org ?? target.Org
        );
        Log(logger, mode, result, target.AppId);

        if (mode != AppCallerMode.Enforce || result.Outcome == AppCallerOutcome.Valid)
        {
            return;
        }

        context.Result =
            result.Outcome == AppCallerOutcome.WrongApp
                ? Problem(
                    StatusCodes.Status403Forbidden,
                    "App token is for another app",
                    $"The {HeaderName} header was issued to an app other than the one that owns the target."
                )
                : Problem(
                    StatusCodes.Status401Unauthorized,
                    "Missing or invalid app token",
                    $"The {HeaderName} header is missing or invalid ({result.Reason})."
                );
    }

    private static bool IsSyncAdapter(IServiceProvider services)
    {
        string? scope = services
            .GetRequiredService<IOptions<GeneralSettings>>()
            .Value.InstanceSyncAdapterScope;
        return !string.IsNullOrEmpty(scope)
            && services.GetRequiredService<IAuthorization>().UserHasRequiredScope(scope);
    }

    private static async Task<Target> ResolveTarget(
        AuthorizationFilterContext context,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var routeValues = context.RouteData.Values;
        if (
            routeValues.TryGetValue("org", out object? org)
            && routeValues.TryGetValue("app", out object? app)
            && org is string orgValue
            && app is string appValue
        )
        {
            return new Target($"{orgValue}/{appValue}", null);
        }

        if (routeValues.TryGetValue("instanceGuid", out object? instanceGuidValue))
        {
            if (!Guid.TryParse(instanceGuidValue?.ToString(), out Guid instanceGuid))
            {
                return Target.Skip("no_instance");
            }

            InstanceInternal? instance = await services
                .GetRequiredService<IInstanceRepository>()
                .GetOne(instanceGuid, false, cancellationToken);

            return instance is null
                ? Target.Skip("no_instance")
                : new Target(instance.AppId, instance.Org);
        }

        string? appId = context.HttpContext.Request.Query["appId"];
        return string.IsNullOrEmpty(appId) ? Target.Skip("no_target") : new Target(appId, null);
    }

    private static string? ReadToken(HttpContext httpContext)
    {
        StringValues values = httpContext.Request.Headers[HeaderName];

        // More than one header value is passed on as one string, which then fails as malformed.
        return values.Count <= 1 ? values.ToString() : string.Join(',', values.ToArray());
    }

    private static string OutcomeTag(AppCallerOutcome outcome) =>
        outcome switch
        {
            AppCallerOutcome.Valid => "valid",
            AppCallerOutcome.Missing => "missing",
            AppCallerOutcome.WrongApp => "wrong_app",
            _ => "invalid",
        };

    private static void Log(
        ILogger logger,
        AppCallerMode mode,
        AppCallerValidationResult result,
        string expectedAppId
    )
    {
        LogLevel level = result.Outcome switch
        {
            AppCallerOutcome.Valid => LogLevel.Debug,
            AppCallerOutcome.Missing when mode == AppCallerMode.Observe => LogLevel.Information,
            _ => LogLevel.Warning,
        };

        logger.Log(
            level,
            "App caller token {Outcome} ({Reason}) for {ExpectedAppId}: org {Org}, app {AppId}, kid {Kid}, mode {Mode}",
            result.Outcome,
            result.Reason,
            expectedAppId,
            result.Org,
            result.AppId,
            result.Kid,
            mode
        );
    }

    private static ObjectResult Problem(int status, string title, string detail) =>
        new(
            new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
            }
        )
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };

    /// <summary>
    /// The app a request targets.
    /// </summary>
    /// <param name="AppId">The target app id, or null when there is nothing to check against.</param>
    /// <param name="Org">The target org when it comes from stored data, so it is safe as a metric tag.</param>
    /// <param name="SkipReason">Why the check is skipped when <paramref name="AppId"/> is null.</param>
    private sealed record Target(string? AppId, string? Org, string SkipReason = "")
    {
        public static Target Skip(string reason) => new(null, null, reason);
    }
}
