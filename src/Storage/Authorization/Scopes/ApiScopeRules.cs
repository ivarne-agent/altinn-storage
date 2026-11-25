using System;
using System.Collections.Generic;
using System.Security.Claims;
using Altinn.Platform.Storage.Interface.Models;

namespace Altinn.Platform.Storage.Authorization.Scopes;

/// <summary>
/// The rules that decide whether a token's scopes give access to an instance endpoint.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A token with the sync adapter scope is accepted on every instance endpoint.</item>
/// <item>A service owner (a token with the <c>urn:altinn:org</c> claim) needs the built-in service owner
/// scope for the level, or the app's <c>apiScopes.serviceOwners</c> scope when the app sets one. The built-in
/// scope is accepted even then, because such a token is assumed to come from the app itself.</item>
/// <item>A user or system user needs the app's <c>apiScopes.users</c> scope for the level when the app sets
/// one, otherwise the built-in user scope. <c>altinn:portal/enduser</c> is always accepted.</item>
/// </list>
/// An unknown app, such as the app of a missing instance, has no scopes of its own. When the app-defined
/// check is off, the app's own scopes are ignored and the built-in rules apply. A token that lacks both the
/// app's scope and the built-in scope is governed by the stricter of the two modes.
/// </remarks>
public static class ApiScopeRules
{
    /// <summary>
    /// The scope of tokens from the Altinn portal, accepted for users on every app.
    /// </summary>
    public const string PortalEndUser = "altinn:portal/enduser";

    /// <summary>
    /// The built-in scope for users reading instances.
    /// </summary>
    public const string InstancesRead = "altinn:instances.read";

    /// <summary>
    /// The built-in scope for users changing instances.
    /// </summary>
    public const string InstancesWrite = "altinn:instances.write";

    /// <summary>
    /// The built-in scope for service owners reading instances.
    /// </summary>
    public const string ServiceOwnerInstancesRead = "altinn:serviceowner/instances.read";

    /// <summary>
    /// The built-in scope for service owners changing instances.
    /// </summary>
    public const string ServiceOwnerInstancesWrite = "altinn:serviceowner/instances.write";

    /// <summary>
    /// The scope of the Dialogporten sync adapter.
    /// </summary>
    public const string SyncAdapter = "altinn:storage/instances.syncadapter";

    private const string _scopeClaim = "urn:altinn:scope";
    private const string _fallbackScopeClaim = "scope";

    /// <summary>
    /// The scopes of the token: the <c>urn:altinn:scope</c> claim, or the <c>scope</c> claim when it is
    /// missing, split on whitespace. Scopes are compared ordinally, as whole values.
    /// </summary>
    public static IReadOnlySet<string> GetScopes(ClaimsPrincipal user)
    {
        string? claim = user.FindFirst(_scopeClaim)?.Value
            ?? user.FindFirst(_fallbackScopeClaim)?.Value;
        if (string.IsNullOrWhiteSpace(claim))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return new HashSet<string>(
            claim.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.Ordinal
        );
    }

    /// <summary>
    /// Whether the token has <paramref name="scope"/> as one of its scopes, see <see cref="GetScopes"/>.
    /// A null or empty scope is never held.
    /// </summary>
    public static bool HasScope(ClaimsPrincipal user, string? scope) =>
        !string.IsNullOrEmpty(scope) && GetScopes(user).Contains(scope);

    /// <summary>
    /// Replaces the <c>[app]</c> and <c>[org]</c> placeholders in a scope configured by an app.
    /// Returns null when no scope is configured.
    /// </summary>
    public static string? ResolvePlaceholders(string? scope, string org, string app)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return null;
        }

        return scope
            .Trim()
            .Replace("[app]", app, StringComparison.Ordinal)
            .Replace("[org]", org, StringComparison.Ordinal);
    }

    /// <summary>
    /// Decides whether <paramref name="scopes"/> give a caller access at <paramref name="access"/> level
    /// to an instance of <paramref name="application"/>.
    /// </summary>
    /// <param name="scopes">The token's scopes.</param>
    /// <param name="isServiceOwner">Whether the token has the <c>urn:altinn:org</c> claim.</param>
    /// <param name="access">The scope level the endpoint requires.</param>
    /// <param name="application">The app, or null when it is unknown.</param>
    /// <param name="builtInMode">The setting for the built-in scopes.</param>
    /// <param name="appMode">The setting for the scopes apps define.</param>
    /// <param name="syncAdapterScope">The sync adapter scope.</param>
    public static ScopeDecision Decide(
        IReadOnlySet<string> scopes,
        bool isServiceOwner,
        ScopeAccess access,
        Application? application,
        ScopeCheckMode builtInMode,
        ScopeCheckMode appMode,
        string syncAdapterScope
    )
    {
        if (!string.IsNullOrEmpty(syncAdapterScope) && scopes.Contains(syncAdapterScope))
        {
            return new(ScopeCheckOutcome.SyncAdapter, access, builtInMode);
        }

        ApiScopesConfiguration? appScopes =
            appMode == ScopeCheckMode.Off ? null : application?.ApiScopes;

        ScopeDecision decision = isServiceOwner
            ? DecideServiceOwner(scopes, access, application, appScopes, builtInMode, appMode)
            : DecideUser(scopes, access, application, appScopes, builtInMode, appMode);

        // An app's own scopes must never give weaker protection than the built-in scopes. When the
        // app-defined check does not refuse, a token that also fails the built-in rule is governed by the
        // stricter of the two modes.
        if (
            decision.Outcome == ScopeCheckOutcome.MissingApp
            && appMode != ScopeCheckMode.Enforce
            && !HasBuiltInScope(scopes, isServiceOwner, access)
        )
        {
            return decision with { Mode = builtInMode > appMode ? builtInMode : appMode };
        }

        return decision;
    }

    private static bool HasBuiltInScope(
        IReadOnlySet<string> scopes,
        bool isServiceOwner,
        ScopeAccess access
    )
    {
        if (isServiceOwner)
        {
            return scopes.Contains(
                access == ScopeAccess.Read ? ServiceOwnerInstancesRead : ServiceOwnerInstancesWrite
            );
        }

        return scopes.Contains(PortalEndUser)
            || scopes.Contains(access == ScopeAccess.Read ? InstancesRead : InstancesWrite);
    }

    private static ScopeDecision DecideServiceOwner(
        IReadOnlySet<string> scopes,
        ScopeAccess access,
        Application? application,
        ApiScopesConfiguration? appScopes,
        ScopeCheckMode builtInMode,
        ScopeCheckMode appMode
    )
    {
        string builtIn =
            access == ScopeAccess.Read ? ServiceOwnerInstancesRead : ServiceOwnerInstancesWrite;
        if (scopes.Contains(builtIn))
        {
            return new(ScopeCheckOutcome.OkServiceOwnerBuiltIn, access, builtInMode);
        }

        string? appScope = AppScope(appScopes?.ServiceOwners, access, application);
        if (appScope is null)
        {
            return new(ScopeCheckOutcome.MissingBuiltIn, access, builtInMode);
        }

        return scopes.Contains(appScope)
            ? new(ScopeCheckOutcome.OkServiceOwnerApp, access, appMode)
            : new(ScopeCheckOutcome.MissingApp, access, appMode);
    }

    private static ScopeDecision DecideUser(
        IReadOnlySet<string> scopes,
        ScopeAccess access,
        Application? application,
        ApiScopesConfiguration? appScopes,
        ScopeCheckMode builtInMode,
        ScopeCheckMode appMode
    )
    {
        string? appScope = AppScope(appScopes?.Users, access, application);
        ScopeCheckMode mode = appScope is null ? builtInMode : appMode;
        string required = appScope ?? (access == ScopeAccess.Read ? InstancesRead : InstancesWrite);

        if (scopes.Contains(PortalEndUser) || scopes.Contains(required))
        {
            return new(ScopeCheckOutcome.Ok, access, mode);
        }

        return new(
            appScope is null ? ScopeCheckOutcome.MissingBuiltIn : ScopeCheckOutcome.MissingApp,
            access,
            mode
        );
    }

    private static string? AppScope(
        ApiScopes? apiScopes,
        ScopeAccess access,
        Application? application
    )
    {
        if (apiScopes is null || application is null)
        {
            return null;
        }

        string[] appIdParts = application.Id.Split('/');
        string org = application.Org ?? appIdParts[0];
        string app = appIdParts.Length > 1 ? appIdParts[1] : appIdParts[0];

        return ResolvePlaceholders(
            access == ScopeAccess.Read ? apiScopes.Read : apiScopes.Write,
            org,
            app
        );
    }
}
