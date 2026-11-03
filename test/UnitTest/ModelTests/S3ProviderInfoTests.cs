using System;
using System.Text.Json;
using Altinn.Platform.Storage.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

public class S3ProviderInfoTests
{
    [Fact]
    public void Serialize_WritesBucketAndRegionOnly_AndRoundTrips()
    {
        StorageAccountProviderInfo info = new S3ProviderInfo("ttd-prod-appsdata", "eu-north-1");

        Assert.Equal(StorageAccountProviderInfo.S3, info.Provider);
        Assert.Equal("s3", info.Provider);
        Assert.Equal("""{"bucket":"ttd-prod-appsdata","region":"eu-north-1"}""", info.Serialize());
        Assert.Equal(info, StorageAccountProviderInfo.Deserialize(info.Provider, info.Serialize()));
    }

    [Fact]
    public void Deserialize_S3Provider_ReturnsS3VariantWhateverTheKeyOrder()
    {
        StorageAccountProviderInfo info = StorageAccountProviderInfo.Deserialize(
            StorageAccountProviderInfo.S3,
            """{"region":"eu-north-1","bucket":"ttd-prod-appsdata"}"""
        );

        Assert.Equal(new S3ProviderInfo("ttd-prod-appsdata", "eu-north-1"), info);
    }

    [Fact]
    public void Deserialize_S3DocumentWithAnotherProvider_Throws()
    {
        Assert.Throws<JsonException>(() =>
            StorageAccountProviderInfo.Deserialize(
                StorageAccountProviderInfo.AzureBlob,
                """{"bucket":"ttd-prod-appsdata","region":"eu-north-1"}"""
            )
        );
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("ttd-prod-appsdata")]
    [InlineData("ttd.prod.appsdata")]
    [InlineData("1bucket9")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijk")]
    public void Constructor_ValidBucket_IsAccepted(string bucket)
    {
        Assert.Equal(bucket, new S3ProviderInfo(bucket, "eu-north-1").Bucket);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijkl")]
    [InlineData("Bucket")]
    [InlineData("-bucket")]
    [InlineData("bucket-")]
    [InlineData(".bucket")]
    [InlineData("bucket.")]
    [InlineData("buck_et")]
    [InlineData("bucket/key")]
    [InlineData("evil.example:9000")]
    [InlineData("bucket\n")]
    [InlineData("")]
    [InlineData(null)]
    public void Constructor_InvalidBucket_Throws(string? bucket)
    {
        Assert.Throws<ArgumentException>(() => new S3ProviderInfo(bucket!, "eu-north-1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("EU-NORTH-1")]
    [InlineData("eu north 1")]
    [InlineData("eu-north-1.evil.example")]
    [InlineData("eu-north-1/x")]
    [InlineData("eu-north-1\n")]
    [InlineData(null)]
    public void Constructor_InvalidRegion_Throws(string? region)
    {
        Assert.Throws<ArgumentException>(() => new S3ProviderInfo("ttd-prod-appsdata", region!));
    }

    [Theory]
    [InlineData("""{"bucket":"ttd-prod-appsdata"}""")]
    [InlineData("""{"region":"eu-north-1"}""")]
    [InlineData("""{"bucket":null,"region":"eu-north-1"}""")]
    [InlineData("null")]
    [InlineData(
        """{"bucket":"ttd-prod-appsdata","region":"eu-north-1","endpoint":"https://evil.example"}"""
    )]
    [InlineData(
        """{"bucket":"ttd-prod-appsdata","region":"eu-north-1","serviceUrl":"http://evil.example"}"""
    )]
    [InlineData(
        """{"bucket":"ttd-prod-appsdata","region":"eu-north-1","accessKey":"a","secretKey":"b"}"""
    )]
    [InlineData("""{"bucket":"Evil.Example","region":"eu-north-1"}""")]
    [InlineData("""{"bucket":"ttd-prod-appsdata","region":"evil.example"}""")]
    public void Deserialize_InvalidS3Document_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() =>
            StorageAccountProviderInfo.Deserialize(StorageAccountProviderInfo.S3, json)
        );
    }
}
