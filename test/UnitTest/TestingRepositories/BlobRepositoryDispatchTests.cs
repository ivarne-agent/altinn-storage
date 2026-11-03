using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Repository.BlobStorage;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

public class BlobRepositoryDispatchTests
{
    private readonly Mock<IBlobStorageProvider> _azureProvider = CreateProvider(
        StorageAccountProviderInfo.AzureBlob
    );

    private readonly Mock<IBlobStorageProvider> _fileSystemProvider = CreateProvider(
        StorageAccountProviderInfo.FileSystem
    );

    private readonly Mock<IBlobStorageProvider> _s3Provider = CreateProvider(
        StorageAccountProviderInfo.S3
    );

    private readonly BlobRepository _repository;

    public BlobRepositoryDispatchTests()
    {
        _repository = new BlobRepository([
            _azureProvider.Object,
            _fileSystemProvider.Object,
            _s3Provider.Object,
        ]);
        _azureProvider.Invocations.Clear();
        _fileSystemProvider.Invocations.Clear();
        _s3Provider.Invocations.Clear();
    }

    [Fact]
    public async Task EveryOperation_AzureAccount_GoesToAzureProvider()
    {
        StorageAccount account = new(1, new AzureBlobProviderInfo("account", "container"));

        await InvokeAll(account);

        VerifyAll(_azureProvider, account, Times.Once());
        VerifyAll(_fileSystemProvider, account, Times.Never());
    }

    [Fact]
    public async Task EveryOperation_FileSystemAccount_GoesToFileSystemProvider()
    {
        StorageAccount account = new(2, new FileSystemProviderInfo("/data/blobs/ttd"));

        await InvokeAll(account);

        VerifyAll(_fileSystemProvider, account, Times.Once());
        VerifyAll(_azureProvider, account, Times.Never());
    }

    [Fact]
    public async Task EveryOperation_S3Account_GoesToS3Provider()
    {
        StorageAccount account = new(3, new S3ProviderInfo("ttd-prod-appsdata", "eu-north-1"));

        await InvokeAll(account);

        VerifyAll(_s3Provider, account, Times.Once());
        VerifyAll(_azureProvider, account, Times.Never());
        VerifyAll(_fileSystemProvider, account, Times.Never());
    }

    [Fact]
    public async Task ReadBlob_ProviderResult_IsReturned()
    {
        StorageAccount account = new(2, new FileSystemProviderInfo("/data/blobs/ttd"));
        MemoryStream stream = new([1, 2, 3]);
        _fileSystemProvider
            .Setup(provider => provider.ReadBlob(account, "path", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream);

        Assert.Same(stream, await _repository.ReadBlob(account, "path"));
    }

    [Fact]
    public async Task EveryOperation_UnknownProvider_ThrowsNotSupported()
    {
        StorageAccount account = new(3, new UnknownProviderInfo());

        await AssertAllNotSupported(account, "UnknownProviderInfo");
        await AssertAllNotSupported(new StorageAccount(4, null!), string.Empty);
        _azureProvider.VerifyNoOtherCalls();
        _fileSystemProvider.VerifyNoOtherCalls();
        _s3Provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EveryOperation_ProviderNotRegistered_ThrowsNotSupported()
    {
        BlobRepository repository = new([_azureProvider.Object]);
        StorageAccount account = new(2, new FileSystemProviderInfo("/data/blobs/ttd"));

        NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            repository.ReadBlob(account, "path")
        );

        Assert.Equal(
            "Storage account provider is not supported: FileSystemProviderInfo",
            exception.Message
        );
    }

    [Fact]
    public void Constructor_TwoProvidersWithSameName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new BlobRepository([_azureProvider.Object, CreateProvider("azure-blob").Object])
        );
    }

    private async Task InvokeAll(StorageAccount account)
    {
        Guid instanceGuid = Guid.Parse("5c1c4fda-4b8b-4a87-9f2f-1a6c6f35e9a1");
        await _repository.WriteBlob(account, Stream.Null, "path");
        await _repository.ReadBlob(account, "path");
        await _repository.DeleteBlob(account, "path");
        await _repository.DeleteBlobsIfExists(account, ["path"]);
        await _repository.DeleteDataBlobs(account, "ttd/app", instanceGuid);
    }

    private static void VerifyAll(
        Mock<IBlobStorageProvider> provider,
        StorageAccount account,
        Times times
    )
    {
        provider.Verify(p => p.WriteBlob(account, Stream.Null, "path"), times);
        provider.Verify(p => p.ReadBlob(account, "path", It.IsAny<CancellationToken>()), times);
        provider.Verify(p => p.DeleteBlob(account, "path"), times);
        provider.Verify(
            p =>
                p.DeleteBlobsIfExists(
                    account,
                    It.Is<IReadOnlyList<string>>(paths => paths.Count == 1 && paths[0] == "path"),
                    It.IsAny<CancellationToken>()
                ),
            times
        );
        provider.Verify(
            p =>
                p.DeleteDataBlobs(
                    account,
                    "ttd/app",
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()
                ),
            times
        );
    }

    private async Task AssertAllNotSupported(StorageAccount account, string typeName)
    {
        Func<Task>[] operations =
        [
            () => _repository.WriteBlob(account, Stream.Null, "path"),
            () => _repository.ReadBlob(account, "path"),
            () => _repository.DeleteBlob(account, "path"),
            () => _repository.DeleteBlobsIfExists(account, ["path"]),
            () => _repository.DeleteDataBlobs(account, "ttd/app", Guid.NewGuid()),
        ];

        foreach (Func<Task> operation in operations)
        {
            NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(
                operation
            );
            Assert.Equal(
                $"Storage account provider is not supported: {typeName}",
                exception.Message
            );
        }
    }

    private static Mock<IBlobStorageProvider> CreateProvider(string name)
    {
        Mock<IBlobStorageProvider> provider = new();
        provider.SetupGet(p => p.Provider).Returns(name);
        return provider;
    }

    private sealed record UnknownProviderInfo : StorageAccountProviderInfo
    {
        public override string Provider => "unknown";
    }
}
