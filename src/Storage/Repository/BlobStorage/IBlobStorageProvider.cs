using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository.BlobStorage;

/// <summary>
/// Stores blobs in the storage accounts of one provider. <see cref="BlobRepository"/> dispatches each
/// call to the provider whose <see cref="Provider"/> equals <see cref="StorageAccountProviderInfo.Provider"/>
/// of the account.
/// </summary>
/// <remarks>
/// The operations have the contract of the matching <see cref="IBlobRepository"/> operations.
/// </remarks>
public interface IBlobStorageProvider
{
    /// <summary>
    /// Gets the provider this instance handles, such as <see cref="StorageAccountProviderInfo.AzureBlob"/>.
    /// </summary>
    string Provider { get; }

    /// <inheritdoc cref="IBlobRepository.WriteBlob"/>
    Task<(long ContentLength, DateTimeOffset LastModified)> WriteBlob(
        StorageAccount account,
        Stream stream,
        string blobStoragePath
    );

    /// <inheritdoc cref="IBlobRepository.ReadBlob"/>
    Task<Stream?> ReadBlob(
        StorageAccount account,
        string blobStoragePath,
        CancellationToken cancellationToken = default
    );

    /// <inheritdoc cref="IBlobRepository.BlobExists"/>
    Task<bool> BlobExists(
        StorageAccount account,
        string blobStoragePath,
        CancellationToken cancellationToken = default
    );

    /// <inheritdoc cref="IBlobRepository.DeleteBlob"/>
    Task<bool> DeleteBlob(StorageAccount account, string blobStoragePath);

    /// <inheritdoc cref="IBlobRepository.DeleteBlobsIfExists"/>
    Task<bool[]> DeleteBlobsIfExists(
        StorageAccount account,
        IReadOnlyList<string> blobStoragePaths,
        CancellationToken cancellationToken = default
    );

    /// <inheritdoc cref="IBlobRepository.DeleteDataBlobs"/>
    Task<bool> DeleteDataBlobs(
        StorageAccount account,
        string appId,
        Guid instanceGuid,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates a URL that reads one blob, and nothing else, until it expires.
    /// </summary>
    /// <param name="account">The storage account holding the blob.</param>
    /// <param name="blobStoragePath">Path to the blob.</param>
    /// <param name="lifetime">How long the URL stays valid.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The URL and when it expires.</returns>
    /// <exception cref="NotSupportedException">The provider cannot hand out URLs to its blobs.</exception>
    Task<(Uri Url, DateTimeOffset ExpiresOn)> GetReadUrl(
        StorageAccount account,
        string blobStoragePath,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default
    );
}
