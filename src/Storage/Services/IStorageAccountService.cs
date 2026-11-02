using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Manages storage account rows.
/// </summary>
public interface IStorageAccountService
{
    /// <summary>
    /// Gets all storage account rows.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The rows, split by whether this build can read their provider info.</returns>
    Task<StorageAccountRows> GetAll(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one storage account row.
    /// </summary>
    /// <param name="id">The row id.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The row in the list that matches whether this build can read it; both lists are empty when no row has the id.</returns>
    Task<StorageAccountRows> Get(short id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a storage account row. A writable row takes the writable flag over from the previous
    /// writable row for the same org and number.
    /// </summary>
    /// <param name="providerInfo">Where the account keeps its blobs.</param>
    /// <param name="org">The blob storage org the account serves.</param>
    /// <param name="storageAccountNumber">The application storage account number the account serves.</param>
    /// <param name="writable">Whether new blob versions for the org and number are written to the account.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The new row.</returns>
    Task<StorageAccountRow> Create(
        StorageAccountProviderInfo providerInfo,
        string org,
        int storageAccountNumber,
        bool writable,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sets the writable flag of a storage account row. A row made writable takes the flag over from
    /// the other writable row for the same org and number.
    /// </summary>
    /// <param name="id">The row id.</param>
    /// <param name="writable">Whether new blob versions for the org and number are written to the account.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The updated row in the list that matches whether this build can read it; both lists are empty when no row has the id.</returns>
    Task<StorageAccountRows> SetWritable(
        short id,
        bool writable,
        CancellationToken cancellationToken = default
    );
}
