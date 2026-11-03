using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository.BlobStorage;
using Altinn.Platform.Storage.UnitTest.Utils;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

/// <summary>
/// The S3 provider against MinIO (<c>docker compose up -d altinn_platform_storage_minio</c>), with a
/// bucket of its own per test.
/// </summary>
public sealed class S3BlobProviderTests : IAsyncLifetime, IDisposable
{
    private readonly S3BlobProvider _provider = CreateProvider(MinioTestBuckets.Settings);
    private readonly AmazonS3Client _client = MinioTestBuckets.CreateClient();
    private string _bucket = null!;
    private StorageAccount _account = null!;

    public async Task InitializeAsync()
    {
        _bucket = await MinioTestBuckets.Create("s3provider");
        _account = new StorageAccount(9, new S3ProviderInfo(_bucket, MinioTestBuckets.Region));
    }

    public async Task DisposeAsync()
    {
        await MinioTestBuckets.Delete(_bucket);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _client.Dispose();
    }

    [Fact]
    public async Task WriteBlob_ThenReadBlob_RoundtripsContentAtObjectKey()
    {
        string blobStoragePath = NewBlobPath("data-elements/version-1");
        byte[] content = Encoding.UTF8.GetBytes($"content-{Guid.NewGuid():N}");
        DateTimeOffset before = DateTimeOffset.UtcNow.AddMinutes(-1);

        (long contentLength, DateTimeOffset lastModified) = await _provider.WriteBlob(
            _account,
            new MemoryStream(content),
            blobStoragePath
        );
        await using Stream? read = await _provider.ReadBlob(_account, blobStoragePath);

        Assert.Equal(content.Length, contentLength);
        Assert.Equal(TimeSpan.Zero, lastModified.Offset);
        Assert.InRange(lastModified, before, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(content, await ReadObject(blobStoragePath));
        Assert.NotNull(read);
        Assert.Equal(content, await ToArray(read));
    }

    [Fact]
    public async Task WriteBlob_ExistingBlob_Overwrites()
    {
        string blobStoragePath = NewBlobPath("data-elements/version-1");
        await Write(blobStoragePath, "first content that is longer");

        (long contentLength, _) = await _provider.WriteBlob(
            _account,
            new MemoryStream(Encoding.UTF8.GetBytes("second")),
            blobStoragePath
        );

        Assert.Equal(6, contentLength);
        Assert.Equal("second", Encoding.UTF8.GetString(await ReadObject(blobStoragePath)));
    }

    [Fact]
    public async Task WriteBlob_LargeStreamOfUnknownLength_UploadsInParts()
    {
        string blobStoragePath = NewBlobPath("data-elements/large");
        byte[] content = RandomNumberGenerator.GetBytes(20 * 1024 * 1024);

        (long contentLength, _) = await _provider.WriteBlob(
            _account,
            new NonSeekableStream(new MemoryStream(content)),
            blobStoragePath
        );
        await using Stream? read = await _provider.ReadBlob(_account, blobStoragePath);

        Assert.Equal(content.Length, contentLength);
        Assert.NotNull(read);
        Assert.Equal(SHA256.HashData(content), SHA256.HashData(await ToArray(read)));
        GetObjectMetadataResponse metadata = await _client.GetObjectMetadataAsync(
            _bucket,
            blobStoragePath
        );
        Assert.Contains("-", metadata.ETag);
    }

    [Fact]
    public async Task WriteBlob_EmptyStream_ReadsBackEmpty()
    {
        string blobStoragePath = NewBlobPath("data-elements/empty");

        (long contentLength, _) = await _provider.WriteBlob(
            _account,
            new MemoryStream(),
            blobStoragePath
        );
        await using Stream? read = await _provider.ReadBlob(_account, blobStoragePath);

        Assert.Equal(0, contentLength);
        Assert.NotNull(read);
        Assert.Empty(await ToArray(read));
    }

    [Fact]
    public async Task ReadBlob_MissingBlob_ReturnsNull()
    {
        Assert.Null(await _provider.ReadBlob(_account, NewBlobPath("missing")));
    }

    [Fact]
    public async Task ReadBlob_MissingBucket_Throws()
    {
        StorageAccount account = new(
            10,
            new S3ProviderInfo($"missing-{Guid.NewGuid():N}", MinioTestBuckets.Region)
        );

        AmazonS3Exception exception = await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            _provider.ReadBlob(account, NewBlobPath("missing"))
        );

        Assert.Equal("NoSuchBucket", exception.ErrorCode);
    }

    [Fact]
    public async Task DeleteBlob_ExistingThenMissing_IsIdempotent()
    {
        string blobStoragePath = NewBlobPath("data-elements/version-1");
        await Write(blobStoragePath, "delete me");

        bool first = await _provider.DeleteBlob(_account, blobStoragePath);
        bool second = await _provider.DeleteBlob(_account, blobStoragePath);

        Assert.True(first);
        Assert.False(second);
        Assert.False(await ObjectExists(blobStoragePath));
    }

    [Fact]
    public async Task DeleteBlobsIfExists_ExistingMissingEmptyAndDuplicatePaths_ReturnsIndexedResults()
    {
        string existingBlob = NewBlobPath("data-elements/existing");
        string otherBlob = NewBlobPath("data-elements/other");
        string missingBlob = NewBlobPath("data-elements/missing");
        await Write(existingBlob, "delete me");
        await Write(otherBlob, "delete me too");

        bool[] result = await _provider.DeleteBlobsIfExists(
            _account,
            [existingBlob, missingBlob, string.Empty, existingBlob, otherBlob]
        );

        Assert.Equal([true, true, false, true, true], result);
        Assert.False(await ObjectExists(existingBlob));
        Assert.False(await ObjectExists(otherBlob));
    }

    [Fact]
    public async Task DeleteDataBlobs_MoreThanOnePage_DeletesTargetInstanceOnly()
    {
        Guid targetInstanceGuid = Guid.NewGuid();
        Guid otherInstanceGuid = Guid.NewGuid();
        string[] targetBlobs = Enumerable
            .Range(0, 1005)
            .Select(index => $"ttd/app/{targetInstanceGuid}/data-elements/version-{index}")
            .ToArray();
        string otherInstanceBlob = $"ttd/app/{otherInstanceGuid}/data-elements/version-1";
        await Parallel.ForEachAsync(
            targetBlobs.Append(otherInstanceBlob),
            new ParallelOptions { MaxDegreeOfParallelism = 16 },
            async (key, cancellationToken) =>
                await _client.PutObjectAsync(
                    new PutObjectRequest
                    {
                        BucketName = _bucket,
                        Key = key,
                        ContentBody = "x",
                    },
                    cancellationToken
                )
        );

        bool result = await _provider.DeleteDataBlobs(_account, "ttd/app", targetInstanceGuid);
        bool missingResult = await _provider.DeleteDataBlobs(
            _account,
            "ttd/app",
            targetInstanceGuid
        );

        Assert.True(result);
        Assert.True(missingResult);
        ListObjectsV2Response remaining = await _client.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = _bucket }
        );
        Assert.Equal([otherInstanceBlob], remaining.S3Objects.Select(item => item.Key));
    }

    [Fact]
    public async Task DeleteDataBlobs_MissingBucket_ReturnsFalse()
    {
        StorageAccount account = new(
            10,
            new S3ProviderInfo($"missing-{Guid.NewGuid():N}", MinioTestBuckets.Region)
        );

        Assert.False(await _provider.DeleteDataBlobs(account, "ttd/app", Guid.NewGuid()));
    }

    [Fact]
    public async Task ReadBlob_AzureAccount_ThrowsNotSupported()
    {
        StorageAccount account = new(1, new AzureBlobProviderInfo("account", "container"));

        NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            _provider.ReadBlob(account, "path")
        );

        Assert.Equal(
            "Storage account provider is not supported: AzureBlobProviderInfo",
            exception.Message
        );
    }

    [Fact]
    public void CreateConfig_ServiceUrl_UsesPathStyleAndSignsForTheRegion()
    {
        AmazonS3Config config = S3BlobProvider.CreateConfig(
            new StorageAccountSettings.S3Settings { ServiceUrl = "http://localhost:9000" },
            "eu-north-1"
        );

        Assert.True(config.ForcePathStyle);
        Assert.Equal("http://localhost:9000/", config.ServiceURL);
        Assert.Equal("eu-north-1", config.AuthenticationRegion);
    }

    [Fact]
    public void CreateConfig_NoServiceUrl_UsesAwsInTheRegion()
    {
        AmazonS3Config config = S3BlobProvider.CreateConfig(new(), "eu-north-1");

        Assert.False(config.ForcePathStyle);
        Assert.Null(config.ServiceURL);
        Assert.Equal(RegionEndpoint.EUNorth1, config.RegionEndpoint);
    }

    private static S3BlobProvider CreateProvider(StorageAccountSettings.S3Settings settings)
    {
        return new S3BlobProvider(
            Options.Create(new StorageAccountSettings { S3 = settings }),
            NullLogger<S3BlobProvider>.Instance
        );
    }

    private static string NewBlobPath(string suffix)
    {
        return $"ttd/app/{Guid.NewGuid()}/{suffix}";
    }

    private static async Task<byte[]> ToArray(Stream stream)
    {
        using MemoryStream copy = new();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    private async Task<bool> ObjectExists(string key)
    {
        try
        {
            await _client.GetObjectMetadataAsync(_bucket, key);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task<byte[]> ReadObject(string key)
    {
        using GetObjectResponse response = await _client.GetObjectAsync(_bucket, key);
        return await ToArray(response.ResponseStream);
    }

    private async Task Write(string blobStoragePath, string content)
    {
        await _provider.WriteBlob(
            _account,
            new MemoryStream(Encoding.UTF8.GetBytes(content)),
            blobStoragePath
        );
    }

    /// <summary>
    /// A stream whose length is unknown, like a request body.
    /// </summary>
    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
