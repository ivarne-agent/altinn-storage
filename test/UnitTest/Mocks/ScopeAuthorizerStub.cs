using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization.Scopes;
using Altinn.Platform.Storage.Models;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.UnitTest.Mocks;

/// <summary>
/// An <see cref="IInstanceScopeAuthorizer"/> that lets every request through, for tests of what happens
/// after the scope check.
/// </summary>
public sealed class ScopeAuthorizerStub : IInstanceScopeAuthorizer
{
    /// <inheritdoc/>
    public InstanceInternal? CheckParty(InstanceInternal? instance, int instanceOwnerPartyId) =>
        instance;

    /// <inheritdoc/>
    public Task<ProblemDetails?> AuthorizeInstance(
        InstanceInternal? instance,
        CancellationToken cancellationToken
    ) => Task.FromResult<ProblemDetails?>(null);

    /// <inheritdoc/>
    public Task<ProblemDetails?> AuthorizeApplication(
        string? appId,
        CancellationToken cancellationToken
    ) => Task.FromResult<ProblemDetails?>(null);

    /// <inheritdoc/>
    public Task<List<InstanceInternal>> FilterByAppScopes(
        List<InstanceInternal> instances,
        CancellationToken cancellationToken
    ) => Task.FromResult(instances);
}
