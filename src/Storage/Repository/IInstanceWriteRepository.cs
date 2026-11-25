using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Writes application instances. It cannot load an instance, so a controller that has it still gets its
/// instances through <see cref="Authorization.IAuthorization"/>.
/// </summary>
public interface IInstanceWriteRepository
{
    /// <summary>
    /// Inserts a new instance.
    /// </summary>
    /// <remarks>
    /// On success, this operation mutates and returns the supplied <paramref name="instance"/>
    /// reference. Its storage-format <see cref="InstanceInternal.Id"/> is retained, or generated
    /// when null; <see cref="InstanceInternal.LastChanged"/> is truncated to microsecond precision;
    /// <see cref="InstanceInternal.Data"/> is cleared because data elements are not inserted by this
    /// operation; <see cref="InstanceInternal.Versions"/> is reset to the initial value (1, 1); and
    /// <see cref="InstanceInternal.InternalId"/> is reset to 0 because the insert operation does not
    /// return the storage row identifier. A subsequent <see cref="IInstanceRepository.GetOne"/> call hydrates that value.
    /// </remarks>
    /// <param name="instance">The storage-domain instance to insert.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <param name="altinnMainVersion">the altinn main version</param>
    /// <returns>The same successfully inserted <paramref name="instance"/> reference.</returns>
    Task<InstanceInternal> Create(
        InstanceInternal instance,
        CancellationToken cancellationToken,
        int altinnMainVersion = 3
    );

    /// <summary>
    /// update existing instance
    /// </summary>
    /// <param name="instance">the instance to update</param>
    /// <param name="updateProperties">a list of which properties should be updated</param>
    /// <param name="expectedInstanceVersion">Expected instance version for optimistic concurrency checks.</param>
    /// <param name="expectedProcessStateVersion">Expected process state version for optimistic concurrency checks.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The updated instance with storage-owned version metadata.</returns>
    Task<InstanceInternal> Update(
        InstanceInternal instance,
        List<string> updateProperties,
        int? expectedInstanceVersion = null,
        int? expectedProcessStateVersion = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Applies a data-values patch without changing process status or storage-owned versions.
    /// </summary>
    /// <remarks>
    /// Null or empty values remove keys. Preconditions fence versioned instance changes, not other
    /// standalone data-values patches. Concurrent writes to the same key use the last committed value.
    /// </remarks>
    /// <param name="instanceGuid">The instance to update.</param>
    /// <param name="dataValues">Only the keys to add, replace, or remove.</param>
    /// <param name="expectedInstanceVersion">Optional expected instance version.</param>
    /// <param name="expectedProcessStateVersion">Optional expected process-state version.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The current instance and storage-owned versions from the mutation transaction.</returns>
    Task<InstanceInternal> UpdateDataValues(
        Guid instanceGuid,
        Dictionary<string, string> dataValues,
        int? expectedInstanceVersion = null,
        int? expectedProcessStateVersion = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Updates only the instance read status without bumping storage-owned versions.
    /// </summary>
    /// <param name="instanceInternal">The instance to update with internal metadata.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The updated instance with current storage-owned version metadata.</returns>
    Task<InstanceInternal> UpdateReadStatus(
        InstanceInternal instanceInternal,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Delets an instance.
    /// </summary>
    /// <param name="instanceGuid">The instance identifier to delete</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>if the item is deleted or not</returns>
    Task<bool> Delete(Guid instanceGuid, CancellationToken cancellationToken);
}
