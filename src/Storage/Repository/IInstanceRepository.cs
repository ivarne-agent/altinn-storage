#nullable disable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// The repository to handle application instances. Controllers load instances through
/// <see cref="Authorization.IAuthorization"/> instead, and use the narrower interfaces it extends.
/// </summary>
public interface IInstanceRepository : IInstanceQueryRepository, IInstanceWriteRepository
{
    /// <summary>
    /// Get an instance for a given instance id
    /// </summary>
    /// <param name="instanceGuid">the instance guid</param>
    /// <param name="includeElements">whether to include data elements</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The instance for the given parameters with internal storage-only fields.</returns>
    Task<InstanceInternal> GetOne(
        Guid instanceGuid,
        bool includeElements,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets hard deleted instances for cleanup
    /// </summary>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>Hard deleted instances</returns>
    Task<List<InstanceInternal>> GetHardDeletedInstances(CancellationToken cancellationToken);

    /// <summary>
    /// Gets hard deleted data elements for cleanup
    /// </summary>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>Hard deleted data elements with cleanup metadata</returns>
    Task<List<DeletedDataElementInternal>> GetHardDeletedDataElements(
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets attached blob versions for all data elements in an instance.
    /// </summary>
    /// <param name="instanceGuid">The instance guid.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>Attached blob versions grouped by storage context</returns>
    Task<List<BlobVersionReferencesInternal>> GetBlobVersionsForInstance(
        Guid instanceGuid,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Gets aged blob version rows without a data element metadata row for cleanup.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>Orphan blob versions grouped by storage context</returns>
    Task<List<BlobVersionReferencesInternal>> GetOrphanBlobVersionsForCleanup(
        CancellationToken cancellationToken
    );
}
