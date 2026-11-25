#nullable disable

using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Mvc;
using Moq.Language.Flow;

namespace Moq;

internal static class AuthorizationMockExtensions
{
    /// <summary>
    /// Makes the <c>GetAuthorizedInstance</c> overloads on the mock behave like the real service over
    /// <paramref name="instanceRepository"/>, without party or scope checks: the instance is loaded with
    /// <see cref="IInstanceRepository.GetOne"/> and the decision is taken by the mock's own
    /// <see cref="IAuthorization.AuthorizeInstanceRequest"/> setup, or by the endpoint's own authorization
    /// for the overload that takes one, so existing setups and verifications keep working for controllers
    /// that go through the combined call.
    /// </summary>
    public static Mock<IAuthorization> DelegateGetAuthorizedInstance(
        this Mock<IAuthorization> mock,
        IInstanceRepository instanceRepository
    )
    {
        mock.Setup(a =>
                a.GetAuthorizedInstance(
                    It.IsAny<int>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                async (
                    int instanceOwnerPartyId,
                    Guid instanceGuid,
                    string action,
                    bool includeElements,
                    CancellationToken cancellationToken
                ) =>
                {
                    InstanceInternal instance = await instanceRepository.GetOne(
                        instanceGuid,
                        includeElements,
                        cancellationToken
                    );
                    ProblemDetails problem = await mock.Object.AuthorizeInstanceRequest(
                        instance,
                        action
                    );
                    if (problem is not null)
                    {
                        return (null, problem);
                    }

                    return instance is null
                        ? (null, AuthorizationProblems.NotFound(instanceOwnerPartyId, instanceGuid))
                        : (instance, null);
                }
            );
        mock.Setup(a =>
                a.GetAuthorizedInstance(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                async (
                    Guid instanceGuid,
                    string action,
                    bool includeElements,
                    CancellationToken cancellationToken
                ) =>
                {
                    InstanceInternal instance = await instanceRepository.GetOne(
                        instanceGuid,
                        includeElements,
                        cancellationToken
                    );
                    ProblemDetails problem = await mock.Object.AuthorizeInstanceRequest(
                        instance,
                        action
                    );
                    if (problem is not null)
                    {
                        return (null, problem);
                    }

                    return instance is null
                        ? (null, AuthorizationProblems.NotFound(instanceGuid))
                        : (instance, null);
                }
            );
        mock.Setup(a =>
                a.GetAuthorizedInstance(
                    It.IsAny<int>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Func<InstanceInternal, Task<ProblemDetails>>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                async (
                    int instanceOwnerPartyId,
                    Guid instanceGuid,
                    Func<InstanceInternal, Task<ProblemDetails>> authorize,
                    bool includeElements,
                    CancellationToken cancellationToken
                ) =>
                {
                    InstanceInternal instance = await instanceRepository.GetOne(
                        instanceGuid,
                        includeElements,
                        cancellationToken
                    );
                    if (instance is null)
                    {
                        return (null, AuthorizationProblems.Forbidden());
                    }

                    ProblemDetails problem = await authorize(instance);
                    return problem is null ? (instance, null) : (null, problem);
                }
            );
        return mock;
    }

    /// <summary>
    /// Creates an <see cref="IAuthorization"/> that permits every instance request and loads instances
    /// from <paramref name="instanceRepository"/>.
    /// </summary>
    public static IAuthorization CreatePermissiveAuthorization(
        IInstanceRepository instanceRepository
    )
    {
        Mock<IAuthorization> mock = new();
        mock.Setup(a =>
                a.AuthorizeInstanceRequest(It.IsAny<InstanceInternal>(), It.IsAny<string>())
            )
            .ReturnsAuthorization(true);
        return mock.DelegateGetAuthorizedInstance(instanceRepository).Object;
    }

    /// <summary>
    /// Answers an authorization setup with no problem when <paramref name="authorized"/> is true, and
    /// with a 403 problem otherwise.
    /// </summary>
    public static IReturnsResult<TMock> ReturnsAuthorization<TMock>(
        this ISetup<TMock, Task<ProblemDetails>> setup,
        bool authorized
    )
        where TMock : class =>
        setup.ReturnsAsync(authorized ? null : AuthorizationProblems.Forbidden());
}
