using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Repository.BlobStorage;

/// <summary>
/// Stores blobs as objects in Amazon S3 buckets, or in an S3-compatible service such as MinIO. A blob
/// is the object <c>{blobStoragePath}</c> in the bucket of the account's <see cref="S3ProviderInfo"/>,
/// so a bucket mirrors the layout of an Azure container.
/// </summary>
/// <remarks>
/// The endpoint and the credentials come from <see cref="StorageAccountSettings.S3Settings"/>; the
/// account supplies only the bucket and the region. The provider never creates buckets. It keeps one
/// client per region.
/// </remarks>
public sealed class S3BlobProvider : IBlobStorageProvider, IDisposable
{
    /// <summary>
    /// The maximum number of keys in one S3 DeleteObjects request.
    /// </summary>
    private const int _deleteObjectsLimit = 1000;

    private readonly StorageAccountSettings.S3Settings _settings;
    private readonly ILogger<S3BlobProvider> _logger;
    private readonly ConcurrentDictionary<string, Lazy<IAmazonS3>> _clients = new(
        StringComparer.Ordinal
    );

    /// <summary>
    /// Initializes a new instance of the <see cref="S3BlobProvider"/> class.
    /// </summary>
    /// <param name="settings">The storage account settings holding the S3 settings.</param>
    /// <param name="logger">The logger.</param>
    public S3BlobProvider(IOptions<StorageAccountSettings> settings, ILogger<S3BlobProvider> logger)
    {
        _settings = settings.Value.S3 ?? new();
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Provider => StorageAccountProviderInfo.S3;

    /// <inheritdoc/>
    public async Task<(long ContentLength, DateTimeOffset LastModified)> WriteBlob(
        StorageAccount account,
        Stream stream,
        string blobStoragePath
    )
    {
        S3ProviderInfo info = GetInfo(account);
        IAmazonS3 client = GetClient(info.Region);

        using (TransferUtility transferUtility = new(client))
        {
            await transferUtility.UploadAsync(
                new TransferUtilityUploadRequest
                {
                    BucketName = info.Bucket,
                    Key = blobStoragePath,
                    InputStream = stream,
                    AutoCloseStream = false,
                }
            );
        }

        GetObjectMetadataResponse metadata = await client.GetObjectMetadataAsync(
            info.Bucket,
            blobStoragePath
        );
        return (metadata.ContentLength, ToDateTimeOffset(metadata.LastModified));
    }

    /// <inheritdoc/>
    public async Task<Stream?> ReadBlob(
        StorageAccount account,
        string blobStoragePath,
        CancellationToken cancellationToken = default
    )
    {
        S3ProviderInfo info = GetInfo(account);
        try
        {
            GetObjectResponse response = await GetClient(info.Region)
                .GetObjectAsync(info.Bucket, blobStoragePath, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception exception) when (exception.ErrorCode == "NoSuchKey")
        {
            _logger.LogWarning(
                "Unable to find a blob based on the given information - {Bucket}: {BlobStoragePath}",
                info.Bucket,
                blobStoragePath
            );

            return null;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// S3 does not report whether a deleted object existed, so the object is looked up first; the
    /// result is whether it existed then.
    /// </remarks>
    public async Task<bool> DeleteBlob(StorageAccount account, string blobStoragePath)
    {
        S3ProviderInfo info = GetInfo(account);
        IAmazonS3 client = GetClient(info.Region);
        if (!await ObjectExists(client, info, blobStoragePath, CancellationToken.None))
        {
            return false;
        }

        await client.DeleteObjectAsync(info.Bucket, blobStoragePath);
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool[]> DeleteBlobsIfExists(
        StorageAccount account,
        IReadOnlyList<string> blobStoragePaths,
        CancellationToken cancellationToken = default
    )
    {
        S3ProviderInfo info = GetInfo(account);
        blobStoragePaths ??= [];
        bool[] results = new bool[blobStoragePaths.Count];
        IAmazonS3 client = GetClient(info.Region);

        for (
            int batchStartIndex = 0;
            batchStartIndex < blobStoragePaths.Count;
            batchStartIndex += _deleteObjectsLimit
        )
        {
            int batchLength = Math.Min(
                _deleteObjectsLimit,
                blobStoragePaths.Count - batchStartIndex
            );
            Dictionary<string, List<int>> indexesByKey = new(StringComparer.Ordinal);
            for (int index = batchStartIndex; index < batchStartIndex + batchLength; index++)
            {
                string blobStoragePath = blobStoragePaths[index];
                if (string.IsNullOrEmpty(blobStoragePath))
                {
                    continue;
                }

                if (!indexesByKey.TryGetValue(blobStoragePath, out List<int>? indexes))
                {
                    indexes = [];
                    indexesByKey[blobStoragePath] = indexes;
                }

                indexes.Add(index);
            }

            if (indexesByKey.Count == 0)
            {
                continue;
            }

            List<DeletedObject> deleted;
            try
            {
                DeleteObjectsResponse response = await client.DeleteObjectsAsync(
                    CreateDeleteRequest(info.Bucket, indexesByKey.Keys),
                    cancellationToken
                );
                deleted = response.DeletedObjects ?? [];
            }
            catch (DeleteObjectsException exception)
            {
                deleted = exception.Response?.DeletedObjects ?? [];
                foreach (DeleteError error in exception.Response?.DeleteErrors ?? [])
                {
                    _logger.LogWarning(
                        "Failed to delete detached blob {BlobStoragePath} in batch operation. Code: {Code}; leaving blob-version row for retry.",
                        error.Key,
                        error.Code
                    );
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to submit blob batch delete operation for {BlobCount} blobs in {Bucket}; leaving blob-version rows for retry.",
                    indexesByKey.Count,
                    info.Bucket
                );
                continue;
            }

            // S3 reports a key that did not exist as deleted, which matches the Azure batch delete
            // treating 404 as done.
            foreach (DeletedObject deletedObject in deleted)
            {
                if (indexesByKey.TryGetValue(deletedObject.Key, out List<int>? indexes))
                {
                    foreach (int index in indexes)
                    {
                        results[index] = true;
                    }
                }
            }
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteDataBlobs(
        StorageAccount account,
        string appId,
        Guid instanceGuid,
        CancellationToken cancellationToken = default
    )
    {
        S3ProviderInfo info = GetInfo(account);
        IAmazonS3 client = GetClient(info.Region);
        ListObjectsV2Request listRequest = new()
        {
            BucketName = info.Bucket,
            Prefix = $"{appId}/{instanceGuid}",
            MaxKeys = _deleteObjectsLimit,
        };

        try
        {
            ListObjectsV2Response page;
            do
            {
                page = await client.ListObjectsV2Async(listRequest, cancellationToken);
                List<string> keys = (page.S3Objects ?? []).Select(item => item.Key).ToList();
                if (keys.Count > 0)
                {
                    await client.DeleteObjectsAsync(
                        CreateDeleteRequest(info.Bucket, keys),
                        cancellationToken
                    );
                }

                listRequest.ContinuationToken = page.NextContinuationToken;
            } while (page.IsTruncated == true);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "BlobService // DeleteDataBlobs // Bucket: {Bucket}",
                info.Bucket
            );
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (Lazy<IAmazonS3> client in _clients.Values)
        {
            if (client.IsValueCreated)
            {
                client.Value.Dispose();
            }
        }

        _clients.Clear();
    }

    private static S3ProviderInfo GetInfo(StorageAccount account)
    {
        return account.ProviderInfo as S3ProviderInfo
            ?? throw new NotSupportedException(
                $"Storage account provider is not supported: {account.ProviderInfo?.GetType().Name}"
            );
    }

    private static DeleteObjectsRequest CreateDeleteRequest(string bucket, IEnumerable<string> keys)
    {
        return new DeleteObjectsRequest
        {
            BucketName = bucket,
            Objects = keys.Select(key => new KeyVersion { Key = key }).ToList(),
        };
    }

    private static async Task<bool> ObjectExists(
        IAmazonS3 client,
        S3ProviderInfo info,
        string blobStoragePath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = info.Bucket, Key = blobStoragePath },
                cancellationToken
            );
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime? lastModified)
    {
        DateTime value = lastModified ?? DateTime.UtcNow;
        return new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero);
    }

    private IAmazonS3 GetClient(string region)
    {
        return _clients.GetOrAdd(region, key => new Lazy<IAmazonS3>(() => CreateClient(key))).Value;
    }

    /// <summary>
    /// Creates the client configuration for a region: AWS S3 in the region, or the configured
    /// S3-compatible service addressed path-style and signing for the region.
    /// </summary>
    internal static AmazonS3Config CreateConfig(
        StorageAccountSettings.S3Settings settings,
        string region
    )
    {
        AmazonS3Config config = new();
        if (string.IsNullOrEmpty(settings.ServiceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(region);
        }
        else
        {
            config.ServiceURL = settings.ServiceUrl;
            config.ForcePathStyle = true;
            config.AuthenticationRegion = region;
        }

        return config;
    }

    private AmazonS3Client CreateClient(string region)
    {
        AmazonS3Config config = CreateConfig(_settings, region);
        return string.IsNullOrEmpty(_settings.AccessKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(
                new BasicAWSCredentials(_settings.AccessKey, _settings.SecretKey),
                config
            );
    }
}
