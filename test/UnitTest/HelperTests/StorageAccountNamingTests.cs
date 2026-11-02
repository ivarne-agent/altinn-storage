using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.HelperTests;

public class StorageAccountNamingTests
{
    private static readonly AzureStorageConfiguration _azureConfiguration = new()
    {
        AccountName = "altinnstorage",
        OrgStorageAccount = "{0}altinndevstorage01",
        OrgStorageContainer = "{0}-dev-appsdata-blob-db",
    };

    [Fact]
    public void PatternDefault_DefaultNumber_UsesUnmodifiedPattern()
    {
        AzureBlobProviderInfo info = StorageAccountNaming.PatternDefault(
            _azureConfiguration,
            "ttd",
            StorageAccountNaming.DefaultStorageAccountNumber
        );

        Assert.Equal(
            new AzureBlobProviderInfo("ttdaltinndevstorage01", "ttd-dev-appsdata-blob-db"),
            info
        );
    }

    [Fact]
    public void PatternDefault_OtherNumber_ReplacesTrailingDigits()
    {
        AzureBlobProviderInfo info = StorageAccountNaming.PatternDefault(
            _azureConfiguration,
            "ttd",
            7
        );

        Assert.Equal(
            new AzureBlobProviderInfo("ttdaltinndevstorage07", "ttd-dev-appsdata-blob-db"),
            info
        );
    }

    [Fact]
    public void PatternDefault_Azurite_UsesConfiguredAccountForEveryNumber()
    {
        AzureStorageConfiguration configuration = new()
        {
            AccountName = "devstoreaccount1",
            BlobEndPoint = "http://127.0.0.1:10000/devstoreaccount1",
            OrgStorageAccount = "{0}altinndevstorage01",
            OrgStorageContainer = "{0}-dev-appsdata-blob-db",
        };

        AzureBlobProviderInfo info = StorageAccountNaming.PatternDefault(configuration, "ttd", 7);

        Assert.Equal(
            new AzureBlobProviderInfo("devstoreaccount1", "ttd-dev-appsdata-blob-db"),
            info
        );
    }

    [Fact]
    public void PatternDefault_UppercaseOrg_GivesLowercaseNames()
    {
        AzureBlobProviderInfo info = StorageAccountNaming.PatternDefault(
            _azureConfiguration,
            "TTD",
            StorageAccountNaming.DefaultStorageAccountNumber
        );

        Assert.Equal(
            new AzureBlobProviderInfo("ttdaltinndevstorage01", "ttd-dev-appsdata-blob-db"),
            info
        );
    }

    [Fact]
    public void FileSystemPatternDefault_DefaultNumber_UsesContainerFolder()
    {
        FileSystemProviderInfo info = StorageAccountNaming.FileSystemPatternDefault(
            _azureConfiguration,
            "/data/blobs",
            "ttd",
            StorageAccountNaming.DefaultStorageAccountNumber
        );

        Assert.Equal(new FileSystemProviderInfo("/data/blobs/ttd-dev-appsdata-blob-db"), info);
    }

    [Fact]
    public void FileSystemPatternDefault_OtherNumber_AppendsNumberToFolder()
    {
        FileSystemProviderInfo info = StorageAccountNaming.FileSystemPatternDefault(
            _azureConfiguration,
            "/data/blobs",
            "ttd",
            7
        );

        Assert.Equal(new FileSystemProviderInfo("/data/blobs/ttd-dev-appsdata-blob-db-07"), info);
    }

    [Fact]
    public void PatternDefault_FileSystemDefaultProvider_ResolverReturnsDirectory()
    {
        StorageAccountSettings settings = new()
        {
            DefaultProvider = StorageAccountProviderInfo.FileSystem,
            FileSystem = new() { RootDirectory = "/data/blobs" },
        };
        IStorageAccountResolver resolver = TestStorageAccounts.CreateResolver(
            configuration: _azureConfiguration,
            settings: settings
        );

        Assert.Equal(
            new StorageAccount(
                null,
                new FileSystemProviderInfo("/data/blobs/ttd-dev-appsdata-blob-db")
            ),
            resolver.PatternDefault("ttd", 1)
        );
        Assert.Equal(
            new StorageAccount(
                null,
                new FileSystemProviderInfo("/data/blobs/ttd-dev-appsdata-blob-db-02")
            ),
            resolver.PatternDefault("ttd", 2)
        );
    }

    [Fact]
    public void PatternDefault_AzureDefaultProvider_ResolverReturnsContainer()
    {
        IStorageAccountResolver resolver = TestStorageAccounts.CreateResolver(
            configuration: _azureConfiguration,
            settings: new StorageAccountSettings()
        );

        Assert.Equal(
            new AzureBlobProviderInfo("ttdaltinndevstorage01", "ttd-dev-appsdata-blob-db"),
            resolver.PatternDefault("ttd", 1).ProviderInfo
        );
    }

    [Fact]
    public void NumberOf_ApplicationWithoutNumber_ReturnsDefault()
    {
        Assert.Equal(1, StorageAccountNaming.NumberOf(new Application()));
    }

    [Fact]
    public void NumberOf_ApplicationWithNumber_ReturnsNumber()
    {
        Assert.Equal(
            3,
            StorageAccountNaming.NumberOf(new Application { StorageAccountNumber = 3 })
        );
    }
}
