using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Authorization.Scopes;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Controllers;

/// <summary>
/// Handles operations for the application instance locks
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="InstanceLockController"/> class
/// </remarks>
/// <param name="authorizationService">gets the instance, with the party and scope checks</param>
/// <param name="instanceLockRepository">the instance lock repository</param>
/// <param name="processAuthorizer">the process authorizer</param>
/// <param name="logger">the logger</param>
[Route("storage/api/v1/instances/{instanceOwnerPartyId:int}/{instanceGuid:guid}/lock")]
[ApiController]
public class InstanceLockController(
    IAuthorization authorizationService,
    IInstanceLockRepository instanceLockRepository,
    IProcessAuthorizer processAuthorizer,
    ILogger<InstanceLockController> logger
) : ControllerBase
{
    private const string _lockTokenHeader = "Altinn-Storage-Lock-Token";

    /// <summary>
    /// Attempts to acquire a lock for an instance.
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance to lock.</param>
    /// <param name="request">The lock request containing expiration time.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The lock response with lock key if successful, or Conflict if lock is already held.</returns>
    [InstanceScopeAccess(ScopeAccess.Write)]
    [Authorize]
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Produces("application/json")]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status403Forbidden,
        "application/problem+json"
    )]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status503ServiceUnavailable,
        "application/problem+json"
    )]
    public async Task<ActionResult<InstanceLockResponse>> AcquireInstanceLock(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        [FromBody] InstanceLockRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.TtlSeconds < 0)
        {
            return Problem(
                detail: "TtlSeconds cannot be negative.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        var userOrOrgNo = User.GetUserOrOrgNo();

        if (userOrOrgNo is null)
        {
            return Problem(
                detail: "User identity could not be determined.",
                statusCode: StatusCodes.Status403Forbidden
            );
        }

        (InstanceInternal? instance, ProblemDetails? problem) =
            await authorizationService.GetAuthorizedInstance(
                instanceOwnerPartyId,
                instanceGuid,
                i => AuthorizeAcquireLock(i, instanceOwnerPartyId),
                includeElements: false,
                cancellationToken
            );
        if (instance is null)
        {
            return problem!.ToResult();
        }

        var (result, lockToken) = await instanceLockRepository.TryAcquireLock(
            instance.InternalId,
            request.TtlSeconds,
            userOrOrgNo,
            cancellationToken
        );

        return result switch
        {
            AcquireLockResult.Success => Ok(
                new InstanceLockResponse { LockToken = lockToken!.CreateToken() }
            ),
            AcquireLockResult.LockAlreadyHeld => Problem(
                detail: "Lock is already held for this instance.",
                statusCode: StatusCodes.Status409Conflict
            ),
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>
    /// Updates TTL on an instance lock.
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance to lock.</param>
    /// <param name="request">The lock request (TTL should be 0 for release).</param>
    /// <param name="lockTokenHeader">The lock token used for authorizing access to the lock.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>NoContent if successful.</returns>
    [InstanceScopeAccess(ScopeAccess.Write)]
    [Authorize]
    [HttpPatch]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [Produces("application/json")]
    public async Task<ActionResult> UpdateInstanceLock(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        [FromBody] InstanceLockRequest request,
        [FromHeader(Name = _lockTokenHeader)] string lockTokenHeader,
        CancellationToken cancellationToken
    )
    {
        LockToken lockToken;
        try
        {
            lockToken = LockToken.ParseToken(lockTokenHeader);
        }
        catch (FormatException e)
        {
            logger.LogWarning(message: "Could not parse lock token.", exception: e);
            return Problem(
                detail: "Could not parse lock token.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        if (request.TtlSeconds < 0)
        {
            return Problem(
                detail: "TtlSeconds cannot be negative.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        // The lock token authorizes the update. The instance must belong to the route's party.
        (InstanceInternal? instance, ProblemDetails? problem) =
            await authorizationService.GetAuthorizedInstance(
                instanceOwnerPartyId,
                instanceGuid,
                i => Task.FromResult(AuthorizeRouteParty(i, instanceOwnerPartyId)),
                includeElements: false,
                cancellationToken
            );
        if (instance is null)
        {
            return problem!.ToResult();
        }

        var result = await instanceLockRepository.TryUpdateLockExpiration(
            lockToken,
            instance.InternalId,
            request.TtlSeconds,
            cancellationToken
        );

        return result switch
        {
            UpdateLockResult.Success => NoContent(),
            UpdateLockResult.LockNotFound => Problem(
                detail: "Lock not found.",
                statusCode: StatusCodes.Status404NotFound
            ),
            UpdateLockResult.LockExpired => Problem(
                detail: "Lock has expired.",
                statusCode: StatusCodes.Status422UnprocessableEntity
            ),
            UpdateLockResult.TokenMismatch => Problem(
                detail: "Lock token is invalid.",
                statusCode: StatusCodes.Status403Forbidden
            ),
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>
    /// The lock endpoints require the instance to belong to the route's party, whatever the mode of the
    /// party check in <see cref="IAuthorization.GetAuthorizedInstance(int, Guid, string, bool, CancellationToken)"/>.
    /// </summary>
    private static ProblemDetails? AuthorizeRouteParty(
        InstanceInternal instance,
        int instanceOwnerPartyId
    )
    {
        return instance.InstanceOwner.PartyId == instanceOwnerPartyId.ToString()
            ? null
            : AuthorizationProblems.Forbidden();
    }

    private async Task<ProblemDetails?> AuthorizeAcquireLock(
        InstanceInternal instance,
        int instanceOwnerPartyId
    )
    {
        if (AuthorizeRouteParty(instance, instanceOwnerPartyId) is { } problem)
        {
            return problem;
        }

        ProblemDetails? lockProblem = await processAuthorizer.AuthorizeInstanceLock(instance);
        if (lockProblem?.Status == StatusCodes.Status403Forbidden)
        {
            lockProblem.Detail = "Not authorized to acquire instance lock.";
        }

        return lockProblem;
    }
}
