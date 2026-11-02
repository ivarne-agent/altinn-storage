#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository.BlobStorage;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Represents an implementation of <see cref="IBlobRepository"/> that passes each call to the
/// <see cref="IBlobStorageProvider"/> of the account's provider.
/// </summary>
public class BlobRepository : IBlobRepository
{
    private readonly IReadOnlyDictionary<string, IBlobStorageProvider> _providers;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlobRepository"/> class.
    /// </summary>
    /// <param name="providers">The blob storage providers, at most one per provider name.</param>
    public BlobRepository(IEnumerable<IBlobStorageProvider> providers)
    {
        _providers = providers.ToDictionary(provider => provider.Provider, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public Task<(long ContentLength, DateTimeOffset LastModified)> WriteBlob(
        StorageAccount account,
        Stream stream,
        string blobStoragePath
    )
    {
        return GetProvider(account).WriteBlob(account, stream, blobStoragePath);
    }

    /// <inheritdoc/>
    public Task<Stream> ReadBlob(
        StorageAccount account,
        string blobStoragePath,
        CancellationToken cancellationToken = default
    )
    {
        return GetProvider(account).ReadBlob(account, blobStoragePath, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<bool> BlobExists(
        StorageAccount account,
        string blobStoragePath,
        CancellationToken cancellationToken = default
    )
    {
        return GetProvider(account).BlobExists(account, blobStoragePath, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteBlob(StorageAccount account, string blobStoragePath)
    {
        return GetProvider(account).DeleteBlob(account, blobStoragePath);
    }

    /// <inheritdoc/>
    public Task<bool[]> DeleteBlobsIfExists(
        StorageAccount account,
        IReadOnlyList<string> blobStoragePaths,
        CancellationToken cancellationToken = default
    )
    {
        return GetProvider(account)
            .DeleteBlobsIfExists(account, blobStoragePaths, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteDataBlobs(
        StorageAccount account,
        string appId,
        Guid instanceGuid,
        CancellationToken cancellationToken = default
    )
    {
        return GetProvider(account)
            .DeleteDataBlobs(account, appId, instanceGuid, cancellationToken);
    }

    private IBlobStorageProvider GetProvider(StorageAccount account)
    {
        if (
            account.ProviderInfo is { } info
            && _providers.TryGetValue(info.Provider, out IBlobStorageProvider provider)
        )
        {
            return provider;
        }

        throw new NotSupportedException(
            $"Storage account provider is not supported: {account.ProviderInfo?.GetType().Name}"
        );
    }
}
