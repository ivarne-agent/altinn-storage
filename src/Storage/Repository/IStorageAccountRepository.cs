using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// The repository for storage account rows.
/// </summary>
public interface IStorageAccountRepository
{
    /// <summary>
    /// Reads all storage account rows. A row whose provider info this build cannot read is returned
    /// as unreadable instead of failing the read, so it fails only the operations that use it.
    /// </summary>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The readable and unreadable rows, each ordered by id.</returns>
    Task<StorageAccountRows> ReadAll(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one storage account row.
    /// </summary>
    /// <param name="id">The row id.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The row in the list that matches whether this build can read it; both lists are empty when no row has the id.</returns>
    Task<StorageAccountRows> ReadOne(short id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a storage account row. A writable row takes the writable flag over from the previous
    /// writable row for the same org and number.
    /// </summary>
    /// <param name="providerInfo">Where the account keeps its blobs.</param>
    /// <param name="org">The blob storage org the account serves.</param>
    /// <param name="storageAccountNumber">The application storage account number the account serves.</param>
    /// <param name="writable">Whether new blob versions for the org and number are written to the account.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The id of the new row.</returns>
    Task<short> Insert(
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
    /// <returns>Whether the row exists.</returns>
    Task<bool> SetWritable(short id, bool writable, CancellationToken cancellationToken = default);
}
