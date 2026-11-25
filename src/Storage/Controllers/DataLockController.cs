using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Authorization.Scopes;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Controllers;

/// <summary>
/// API for handling locking and unlocking of data elements
/// </summary>
[Route(
    "storage/api/v1/instances/{instanceOwnerPartyId:int}/{instanceGuid:guid}/data/{dataGuid:guid}/lock"
)]
[ApiController]
public class DataLockController : ControllerBase
{
    private readonly IDataRepository _dataRepository;
    private readonly IAuthorization _authorizationService;
    private readonly IProcessAuthorizer _processAuthorizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataLockController"/> class
    /// </summary>
    /// <param name="dataRepository">the data repository handler</param>
    /// <param name="authorizationService">the authorization service.</param>
    /// <param name="processAuthorizer">the process authorizer.</param>
    public DataLockController(
        IDataRepository dataRepository,
        IAuthorization authorizationService,
        IProcessAuthorizer processAuthorizer
    )
    {
        _dataRepository = dataRepository;
        _authorizationService = authorizationService;
        _processAuthorizer = processAuthorizer;
    }

    /// <summary>
    /// Locks a data element
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance that the data element is associated with.</param>
    /// <param name="dataGuid">The id of the data element to lock.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>DataElement that was locked</returns>
    [InstanceScopeAccess(ScopeAccess.Write)]
    [Authorize]
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status201Created)]
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
    public async Task<ActionResult<DataElement>> Lock(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        Guid dataGuid,
        CancellationToken cancellationToken
    )
    {
        (InstanceInternal? instance, ProblemDetails? lockProblem) =
            await _authorizationService.GetAuthorizedInstance(
                instanceOwnerPartyId,
                instanceGuid,
                _processAuthorizer.AuthorizeDataElementLock,
                includeElements: true,
                cancellationToken
            );
        if (instance is null)
        {
            return lockProblem!.ToResult();
        }

        DataElementInternal? dataElement = instance.Data.FirstOrDefault(d => d.Id == dataGuid);

        if (dataElement?.Locked is true)
        {
            VersionPreconditionHelper.WriteVersionResponseHeaders(Response, instance);
            return Ok(dataElement.ToApiModel());
        }

        try
        {
            DataElementWriteResult updatedDataElement = await _dataRepository.UpdateLockStatus(
                instanceGuid,
                dataGuid,
                true,
                cancellationToken: cancellationToken
            );
            VersionPreconditionHelper.WriteVersionResponseHeaders(
                Response,
                updatedDataElement.Versions
            );
            DataElement response = updatedDataElement.DataElement.ToApiModel();
            return Created(response.Id, response);
        }
        catch (ProcessStatusConflictException e)
        {
            return Conflict(e.Message);
        }
        catch (RepositoryException e)
        {
            return e.StatusCodeSuggestion != null
                ? StatusCode((int)e.StatusCodeSuggestion)
                : StatusCode(500);
        }
    }

    /// <summary>
    /// Unlocks a data element
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance that the data element is associated with.</param>
    /// <param name="dataGuid">The id of the data element to unlock.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>DataElement that was unlocked</returns>
    [InstanceScopeAccess(ScopeAccess.Write)]
    [Authorize]
    [HttpDelete]
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
    public async Task<ActionResult<DataElement>> Unlock(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        Guid dataGuid,
        CancellationToken cancellationToken
    )
    {
        (_, ProblemDetails? unlockProblem) = await _authorizationService.GetAuthorizedInstance(
            instanceOwnerPartyId,
            instanceGuid,
            instance =>
                _authorizationService.AuthorizeAnyOfInstanceActions(
                    instance,
                    [AuthorizationActions.Write, "unlock", AuthorizationActions.Reject]
                ),
            includeElements: false,
            cancellationToken
        );
        if (unlockProblem is not null)
        {
            return unlockProblem.ToResult();
        }

        try
        {
            DataElementWriteResult updatedDataElement = await _dataRepository.UpdateLockStatus(
                instanceGuid,
                dataGuid,
                false,
                cancellationToken: cancellationToken
            );
            VersionPreconditionHelper.WriteVersionResponseHeaders(
                Response,
                updatedDataElement.Versions
            );
            return Ok(updatedDataElement.DataElement.ToApiModel());
        }
        catch (ProcessStatusConflictException e)
        {
            return Conflict(e.Message);
        }
        catch (RepositoryException e)
        {
            return e.StatusCodeSuggestion != null
                ? StatusCode((int)e.StatusCodeSuggestion)
                : StatusCode(500);
        }
    }
}
