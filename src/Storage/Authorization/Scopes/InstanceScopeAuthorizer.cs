using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Authorization.Scopes;

/// <summary>
/// Checks API scopes for instance endpoints, see <see cref="IInstanceScopeAuthorizer"/>.
/// </summary>
/// <param name="httpContextAccessor">Gives the request, its endpoint and its user.</param>
/// <param name="applicationRepository">Loads the app metadata with the app's <c>apiScopes</c>.</param>
/// <param name="settings">The modes of the scope checks and the sync adapter scope.</param>
/// <param name="logger">The logger.</param>
public sealed partial class InstanceScopeAuthorizer(
    IHttpContextAccessor httpContextAccessor,
    IApplicationRepository applicationRepository,
    IOptions<GeneralSettings> settings,
    ILogger<InstanceScopeAuthorizer> logger
) : IInstanceScopeAuthorizer
{
    private readonly GeneralSettings _settings = settings.Value;

    private ScopeCheckMode BuiltInMode => _settings.InstanceBuiltInScopeCheck;

    private ScopeCheckMode AppMode => _settings.InstanceAppScopeCheck;

    private string SyncAdapterScope => _settings.InstanceSyncAdapterScope ?? string.Empty;

    /// <inheritdoc/>
    public InstanceInternal? CheckParty(InstanceInternal? instance, int instanceOwnerPartyId)
    {
        if (
            instance is null
            || BuiltInMode == ScopeCheckMode.Off
            || instance.InstanceOwner?.PartyId == instanceOwnerPartyId.ToString()
        )
        {
            return instance;
        }

        LogPartyMismatch(
            logger,
            instance.Id,
            instance.InstanceOwner?.PartyId,
            instanceOwnerPartyId,
            BuiltInMode,
            GetHttpContext().GetEndpoint()?.DisplayName
        );

        return BuiltInMode == ScopeCheckMode.Enforce ? null : instance;
    }

    /// <inheritdoc/>
    public async Task<ProblemDetails?> AuthorizeInstance(
        InstanceInternal? instance,
        CancellationToken cancellationToken
    )
    {
        HttpContext httpContext = GetHttpContext();
        if (IsOff() || GetRequiredAccess(httpContext) is not { } access)
        {
            return null;
        }

        Application? application = instance is null
            ? null
            : await LoadApplication(instance.AppId, cancellationToken);
        return Apply(httpContext, Decide(httpContext.User, access, application), instance?.AppId);
    }

    /// <inheritdoc/>
    public async Task<ProblemDetails?> AuthorizeApplication(
        string? appId,
        CancellationToken cancellationToken
    )
    {
        HttpContext httpContext = GetHttpContext();
        if (IsOff() || GetRequiredAccess(httpContext) is not { } access)
        {
            return null;
        }

        Application? application = await LoadApplication(appId, cancellationToken);
        return Apply(httpContext, Decide(httpContext.User, access, application), appId);
    }

    /// <inheritdoc/>
    public async Task<List<InstanceInternal>> FilterByAppScopes(
        List<InstanceInternal> instances,
        CancellationToken cancellationToken
    )
    {
        HttpContext httpContext = GetHttpContext();
        ClaimsPrincipal user = httpContext.User;
        if (
            AppMode == ScopeCheckMode.Off
            || instances.Count == 0
            || GetRequiredAccess(httpContext) is not { } access
            || user.GetOrg() is not null
        )
        {
            return instances;
        }

        HashSet<string> refusedApps = new(StringComparer.Ordinal);
        foreach (string appId in instances.Select(i => i.AppId).Distinct(StringComparer.Ordinal))
        {
            Application? application = await LoadApplication(appId, cancellationToken);
            ScopeDecision decision = Decide(user, access, application);
            if (decision.Outcome == ScopeCheckOutcome.MissingApp)
            {
                ScopeCheckTelemetry.LogMissingScope(logger, httpContext, decision, appId);
                if (decision.Refuses)
                {
                    refusedApps.Add(appId);
                }
            }
        }

        return refusedApps.Count == 0
            ? instances
            : instances.Where(i => !refusedApps.Contains(i.AppId)).ToList();
    }

    private bool IsOff() => BuiltInMode == ScopeCheckMode.Off && AppMode == ScopeCheckMode.Off;

    private ScopeDecision Decide(
        ClaimsPrincipal user,
        ScopeAccess access,
        Application? application
    ) =>
        ApiScopeRules.Decide(
            ApiScopeRules.GetScopes(user),
            user.GetOrg() is not null,
            access,
            application,
            BuiltInMode,
            AppMode,
            SyncAdapterScope
        );

    private ProblemDetails? Apply(HttpContext httpContext, ScopeDecision decision, string? appId)
    {
        ScopeCheckTelemetry.Record(httpContext, decision);
        if (decision.IsObserved && decision.IsMissing)
        {
            ScopeCheckTelemetry.LogMissingScope(logger, httpContext, decision, appId);
        }

        return decision.Refuses ? AuthorizationProblems.InsufficientScope(decision.Access) : null;
    }

    private async Task<Application?> LoadApplication(
        string? appId,
        CancellationToken cancellationToken
    )
    {
        // The app's own scopes are ignored when the app-defined check is off.
        if (AppMode == ScopeCheckMode.Off || string.IsNullOrEmpty(appId))
        {
            return null;
        }

        string[] parts = appId.Split('/');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            return null;
        }

        return await applicationRepository.FindOne(appId, parts[0], cancellationToken);
    }

    private HttpContext GetHttpContext() =>
        httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("API scopes are checked only within a request.");

    /// <summary>
    /// The scope level the endpoint declares, or null when the endpoint has a fixed scope or is excluded.
    /// </summary>
    private static ScopeAccess? GetRequiredAccess(HttpContext httpContext)
    {
        Endpoint? endpoint = httpContext.GetEndpoint();
        EndpointMetadataCollection? metadata = endpoint?.Metadata;
        if (metadata?.GetMetadata<InstanceScopeAccessAttribute>() is { } attribute)
        {
            return attribute.Access;
        }

        if (
            metadata?.GetMetadata<RequireScopeAttribute>() is not null
            || metadata?.GetMetadata<ExcludeFromScopeCheckAttribute>() is not null
        )
        {
            return null;
        }

        throw new InvalidOperationException(
            $"The endpoint '{endpoint?.DisplayName}' declares no API scope requirement."
        );
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Instance {InstanceGuid} belongs to party {InstancePartyId}, not to the party {RoutePartyId} in the route of {Endpoint}. Mode: {Mode}"
    )]
    private static partial void LogPartyMismatch(
        ILogger logger,
        Guid instanceGuid,
        string? instancePartyId,
        int routePartyId,
        ScopeCheckMode mode,
        string? endpoint
    );
}
