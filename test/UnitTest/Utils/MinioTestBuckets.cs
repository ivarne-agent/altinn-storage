using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Repository.BlobStorage;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace Altinn.Platform.Storage.UnitTest.Utils;

/// <summary>
/// Buckets in the MinIO test container, reached with the <c>StorageAccounts:S3</c> settings of
/// <c>appsettings.unittest.json</c> pointed at <see cref="TestContainers.MinioServiceUrl"/>. The S3
/// provider never creates buckets, so tests create their own and delete them afterwards.
/// </summary>
public static class MinioTestBuckets
{
    /// <summary>
    /// The region the test buckets are signed for.
    /// </summary>
    public const string Region = "us-east-1";

    private static readonly Lazy<StorageAccountSettings.S3Settings> _settings = new(() =>
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile(ServiceUtil.GetAppsettingsPath())
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["StorageAccounts:S3:ServiceUrl"] = TestContainers.MinioServiceUrl,
                }
            )
            .Build();
        return config.GetSection("StorageAccounts:S3").Get<StorageAccountSettings.S3Settings>()
            ?? throw new InvalidOperationException("StorageAccounts:S3 is not configured.");
    });

    /// <summary>
    /// Gets the S3 settings of the unit tests.
    /// </summary>
    public static StorageAccountSettings.S3Settings Settings => _settings.Value;

    /// <summary>
    /// Creates a client for the MinIO service. The caller disposes it.
    /// </summary>
    public static AmazonS3Client CreateClient()
    {
        return new AmazonS3Client(
            new BasicAWSCredentials(Settings.AccessKey, Settings.SecretKey),
            S3BlobProvider.CreateConfig(Settings, Region)
        );
    }

    /// <summary>
    /// Creates a bucket with a unique name.
    /// </summary>
    public static async Task<string> Create(string prefix)
    {
        string bucket = $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(63, prefix.Length + 33)];
        using AmazonS3Client client = CreateClient();
        await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket });
        return bucket;
    }

    /// <summary>
    /// Deletes a bucket and every object in it.
    /// </summary>
    public static async Task Delete(string bucket)
    {
        using AmazonS3Client client = CreateClient();
        ListObjectsV2Request request = new() { BucketName = bucket };
        ListObjectsV2Response page;
        do
        {
            page = await client.ListObjectsV2Async(request);
            foreach (S3Object item in page.S3Objects ?? [])
            {
                await client.DeleteObjectAsync(bucket, item.Key);
            }

            request.ContinuationToken = page.NextContinuationToken;
        } while (page.IsTruncated == true);

        await client.DeleteBucketAsync(bucket);
    }
}
