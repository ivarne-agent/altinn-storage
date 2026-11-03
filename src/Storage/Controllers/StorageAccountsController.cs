using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.OpenApi;
using Altinn.Platform.Storage.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Controllers;

/// <summary>
/// Administers the storage accounts blobs are written to and read from. For operations only.
/// </summary>
/// <param name="storageAccountService">Manages the storage account rows.</param>
[Route("storage/api/v1/admin/storageaccounts")]
[ApiController]
[Authorize(Policy = AuthzConstants.POLICY_SCOPE_STORAGEACCOUNTS_ADMIN)]
[ExcludeFromPublicStorageApi]
[Produces("application/json")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class StorageAccountsController(IStorageAccountService storageAccountService)
    : ControllerBase
{
    /// <summary>
    /// Lists all storage accounts. An account whose provider info this build cannot read is listed
    /// with an error instead of its provider info.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The storage accounts ordered by id.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<StorageAccountResponse>>> GetAll(
        CancellationToken cancellationToken
    )
    {
        return StorageAccountResponse.From(await storageAccountService.GetAll(cancellationToken));
    }

    /// <summary>
    /// Gets one storage account.
    /// </summary>
    /// <param name="id">The storage account id.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The storage account.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StorageAccountResponse>> Get(
        short id,
        CancellationToken cancellationToken
    )
    {
        StorageAccountRows rows = await storageAccountService.Get(id, cancellationToken);
        return SingleOrNotFound(id, rows);
    }

    /// <summary>
    /// Adds a storage account. A writable account takes the writable flag over from the current
    /// writable account for the same org and storage account number, so new blob versions are written
    /// to it from then on. A concurrent hand-over for the same pair answers 409 Conflict; retry it.
    /// </summary>
    /// <param name="request">The storage account.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The new storage account.</returns>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StorageAccountResponse>> Create(
        [FromBody] StorageAccountRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!request.TryValidate(out StorageAccountProviderInfo? providerInfo, out string? error))
        {
            return Problem(detail: error, statusCode: StatusCodes.Status400BadRequest);
        }

        StorageAccountRow row;
        try
        {
            row = await storageAccountService.Create(
                providerInfo,
                request.Org!,
                request.StorageAccountNumber,
                request.Writable ?? true,
                cancellationToken
            );
        }
        catch (StorageAccountWritableConflictException exception)
        {
            return ConflictProblem(exception);
        }

        return CreatedAtAction(nameof(Get), new { id = row.Id }, StorageAccountResponse.From(row));
    }

    /// <summary>
    /// Sets whether new blob versions for the storage account's org and storage account number are
    /// written to it. Making it writable takes the flag over from the other writable account for the pair;
    /// a concurrent hand-over for the same pair answers 409 Conflict, so retry it.
    /// </summary>
    /// <param name="id">The storage account id.</param>
    /// <param name="request">The writable flag.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The updated storage account.</returns>
    [HttpPut("{id}/writable")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StorageAccountResponse>> SetWritable(
        short id,
        [FromBody] StorageAccountWritableRequest request,
        CancellationToken cancellationToken
    )
    {
        StorageAccountRows rows;
        try
        {
            rows = await storageAccountService.SetWritable(
                id,
                request.Writable!.Value,
                cancellationToken
            );
        }
        catch (StorageAccountWritableConflictException exception)
        {
            return ConflictProblem(exception);
        }

        return SingleOrNotFound(id, rows);
    }

    private ActionResult<StorageAccountResponse> SingleOrNotFound(short id, StorageAccountRows rows)
    {
        StorageAccountResponse? response = StorageAccountResponse.From(rows).SingleOrDefault();
        return response is null ? NotFoundProblem(id) : response;
    }

    private ObjectResult NotFoundProblem(short id) =>
        Problem(
            detail: $"Storage account {id} does not exist.",
            statusCode: StatusCodes.Status404NotFound
        );

    private ObjectResult ConflictProblem(StorageAccountWritableConflictException exception) =>
        Problem(detail: exception.Message, statusCode: StatusCodes.Status409Conflict);
}
