using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Reads and updates the storage account recorded on blob versions, for moving blobs between accounts.
/// </summary>
public interface IBlobVersionRepointRepository
{
    /// <summary>
    /// Reads a page of the blob versions recorded on a storage account, in id order.
    /// </summary>
    /// <param name="fromAccountId">
    /// The storage account id, or null for the pattern default of <paramref name="org"/> and
    /// <paramref name="storageAccountNumber"/>.
    /// </param>
    /// <param name="org">The blob storage org of the pattern default; ignored when <paramref name="fromAccountId"/> is set.</param>
    /// <param name="storageAccountNumber">
    /// The storage account number of the pattern default, where rows without a number count as 1;
    /// ignored when <paramref name="fromAccountId"/> is set.
    /// </param>
    /// <param name="appId">Only versions of this application, or null for all.</param>
    /// <param name="afterId">The page starts after this id; <see cref="Guid.Empty"/> starts at the beginning.</param>
    /// <param name="limit">The maximum number of rows.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The rows ordered by id.</returns>
    Task<IReadOnlyList<BlobVersionRepointRow>> ReadPage(
        short? fromAccountId,
        string? org,
        int? storageAccountNumber,
        string? appId,
        Guid afterId,
        int limit,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Records another storage account on a blob version, provided it still names the expected one.
    /// </summary>
    /// <param name="id">The blob version id.</param>
    /// <param name="fromAccountId">The storage account id the row must name, or null for the pattern default.</param>
    /// <param name="toAccountId">The storage account id to record.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>Whether the row was changed.</returns>
    Task<bool> UpdateAccount(
        Guid id,
        short? fromAccountId,
        short toAccountId,
        CancellationToken cancellationToken = default
    );
}
