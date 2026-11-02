using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Manages storage account rows, dropping the resolver cache after a change so write routing on this
/// instance follows it at once.
/// </summary>
public sealed class StorageAccountService(
    IStorageAccountRepository repository,
    IStorageAccountResolver resolver
) : IStorageAccountService
{
    /// <inheritdoc/>
    public Task<StorageAccountRows> GetAll(CancellationToken cancellationToken = default) =>
        repository.ReadAll(cancellationToken);

    /// <inheritdoc/>
    public Task<StorageAccountRows> Get(short id, CancellationToken cancellationToken = default) =>
        repository.ReadOne(id, cancellationToken);

    /// <inheritdoc/>
    public async Task<StorageAccountRow> Create(
        StorageAccountProviderInfo providerInfo,
        string org,
        int storageAccountNumber,
        bool writable,
        CancellationToken cancellationToken = default
    )
    {
        short id = await repository.Insert(
            providerInfo,
            org,
            storageAccountNumber,
            writable,
            cancellationToken
        );
        resolver.Invalidate();
        StorageAccountRows rows = await repository.ReadOne(id, cancellationToken);
        return rows.Readable.SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"Storage account {id} was not readable after insert."
            );
    }

    /// <inheritdoc/>
    public async Task<StorageAccountRows> SetWritable(
        short id,
        bool writable,
        CancellationToken cancellationToken = default
    )
    {
        if (!await repository.SetWritable(id, writable, cancellationToken))
        {
            return new StorageAccountRows([], []);
        }

        resolver.Invalidate();
        return await repository.ReadOne(id, cancellationToken);
    }
}
