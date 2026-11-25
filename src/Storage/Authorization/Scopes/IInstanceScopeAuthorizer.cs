using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Authorization.Scopes;

/// <summary>
/// Checks the API scopes of the current request against the scope level its endpoint declares with
/// <see cref="InstanceScopeAccessAttribute"/>, by the rules in <see cref="ApiScopeRules"/>. The decision is
/// made without asking Altinn Authorization, recorded on the request metric and, when a scope is missing,
/// logged. Whether a missing scope is refused depends on the mode of the requirement.
/// </summary>
public interface IInstanceScopeAuthorizer
{
    /// <summary>
    /// Checks that <paramref name="instance"/> belongs to the party in the route. An instance of another
    /// party is logged, and treated as missing when the built-in scope check is enforced.
    /// </summary>
    /// <returns>The instance, or null when it is missing or treated as missing.</returns>
    InstanceInternal? CheckParty(InstanceInternal? instance, int instanceOwnerPartyId);

    /// <summary>
    /// Checks the scopes for the endpoint's level against the app of <paramref name="instance"/>. A missing
    /// instance has an unknown app, so the built-in scopes are required. Endpoints with a fixed scope
    /// (<see cref="RequireScopeAttribute"/>) or an exclusion are not checked here. An endpoint without any
    /// of the attributes fails with an exception.
    /// </summary>
    /// <returns>Null when the request may continue, otherwise a 403 problem.</returns>
    Task<ProblemDetails?> AuthorizeInstance(
        InstanceInternal? instance,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Checks the scopes for the endpoint's level against the app <paramref name="appId"/>, for endpoints
    /// that have no instance yet. Without an app id, or for an unknown app, the built-in scopes are required.
    /// </summary>
    /// <returns>Null when the request may continue, otherwise a 403 problem.</returns>
    Task<ProblemDetails?> AuthorizeApplication(string? appId, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the instances of apps whose own user scopes the token lacks, for a user's search across
    /// apps. When the app-defined scope check is only observed, the instances are logged but kept.
    /// </summary>
    Task<List<InstanceInternal>> FilterByAppScopes(
        List<InstanceInternal> instances,
        CancellationToken cancellationToken
    );
}
