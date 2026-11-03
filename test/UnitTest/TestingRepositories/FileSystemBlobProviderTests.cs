using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository.BlobStorage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

public sealed class FileSystemBlobProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "file-system-blobs",
        Guid.NewGuid().ToString("N")
    );

    private readonly string _directory;
    private readonly StorageAccount _account;
    private readonly FileSystemBlobProvider _provider;

    public FileSystemBlobProviderTests()
    {
        _directory = Path.Combine(_root, "ttd-dev-appsdata-blob-db");
        _account = new StorageAccount(7, new FileSystemProviderInfo(_directory));
        _provider = CreateProvider(_root);
    }

    private readonly string _outside = Path.Combine(
        Path.GetTempPath(),
        "file-system-blobs-outside",
        Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        // A root that is itself a symbolic link is removed as a link, leaving its target to the
        // outside cleanup below.
        if (new DirectoryInfo(_root).LinkTarget is not null)
        {
            Directory.Delete(_root);
        }
        else if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        if (Directory.Exists(_outside))
        {
            Directory.Delete(_outside, recursive: true);
        }
    }

    private string CreateOutsideDirectory()
    {
        return Directory.CreateDirectory(_outside).FullName;
    }

    [Fact]
    public async Task WriteBlob_ThenReadBlob_RoundtripsContentAtDirectoryPath()
    {
        string blobStoragePath = NewBlobPath("data-elements/version-1");
        byte[] content = Encoding.UTF8.GetBytes($"content-{Guid.NewGuid():N}");

        (long contentLength, DateTimeOffset lastModified) = await _provider.WriteBlob(
            _account,
            new MemoryStream(content),
            blobStoragePath
        );
        await using Stream? read = await _provider.ReadBlob(_account, blobStoragePath);

        string file = Path.Combine(_directory, blobStoragePath);
        Assert.Equal(content.Length, contentLength);
        Assert.Equal(File.GetLastWriteTimeUtc(file), lastModified.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, lastModified.Offset);
        Assert.Equal(content, await File.ReadAllBytesAsync(file));
        Assert.NotNull(read);
        using MemoryStream copy = new();
        await read.CopyToAsync(copy);
        Assert.Equal(content, copy.ToArray());
    }

    [Fact]
    public async Task WriteBlob_ExistingBlob_OverwritesAndLeavesNoTemporaryFile()
    {
        string blobStoragePath = NewBlobPath("data-elements/version-1");
        await Write(blobStoragePath, "first content that is longer");

        (long contentLength, _) = await _provider.WriteBlob(
            _account,
            new MemoryStream(Encoding.UTF8.GetBytes("second")),
            blobStoragePath
        );

        string file = Path.Combine(_directory, blobStoragePath);
        Assert.Equal(6, contentLength);
        Assert.Equal("second", await File.ReadAllTextAsync(file));
        Assert.Equal([file], Directory.GetFiles(Path.GetDirectoryName(file)!));
    }

    [Fact]
    public async Task GetReadUrl_ThrowsNotSupported()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            _provider.GetReadUrl(_account, "ttd/app/instance/blob", TimeSpan.FromMinutes(5))
        );
    }

    [Fact]
    public async Task ReadBlob_MissingBlob_ReturnsNull()
    {
        Assert.Null(await _provider.ReadBlob(_account, NewBlobPath("missing")));
    }

    [Fact]
    public async Task DeleteBlob_ExistingThenMissing_IsIdempotent()
    {
        string blobStoragePath = NewBlobPath("data-elements/version-delete");
        await Write(blobStoragePath, "delete me");

        bool firstDelete = await _provider.DeleteBlob(_account, blobStoragePath);
        bool secondDelete = await _provider.DeleteBlob(_account, blobStoragePath);

        Assert.True(firstDelete);
        Assert.False(secondDelete);
        Assert.False(File.Exists(Path.Combine(_directory, blobStoragePath)));
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
        Assert.False(File.Exists(Path.Combine(_directory, existingBlob)));
        Assert.False(File.Exists(Path.Combine(_directory, otherBlob)));
    }

    [Fact]
    public async Task DeleteDataBlobs_DeletesTargetInstanceDirectoryOnly()
    {
        Guid targetInstanceGuid = Guid.NewGuid();
        Guid otherInstanceGuid = Guid.NewGuid();
        string firstTargetBlob = $"ttd/app/{targetInstanceGuid}/data-elements/version-1";
        string secondTargetBlob = $"ttd/app/{targetInstanceGuid}/data/legacy";
        string otherInstanceBlob = $"ttd/app/{otherInstanceGuid}/data-elements/version-2";
        await Write(firstTargetBlob, "first target");
        await Write(secondTargetBlob, "second target");
        await Write(otherInstanceBlob, "other instance");

        bool result = await _provider.DeleteDataBlobs(_account, "ttd/app", targetInstanceGuid);
        bool missingResult = await _provider.DeleteDataBlobs(
            _account,
            "ttd/app",
            targetInstanceGuid
        );

        Assert.True(result);
        Assert.True(missingResult);
        Assert.False(Directory.Exists(Path.Combine(_directory, $"ttd/app/{targetInstanceGuid}")));
        Assert.True(File.Exists(Path.Combine(_directory, otherInstanceBlob)));
    }

    [Theory]
    [InlineData("../escaped")]
    [InlineData("ttd/app/../../../escaped")]
    [InlineData("..")]
    public async Task EveryOperation_PathEscapingDirectory_Throws(string blobStoragePath)
    {
        Func<Task>[] operations =
        [
            () => _provider.WriteBlob(_account, new MemoryStream([1]), blobStoragePath),
            () => _provider.ReadBlob(_account, blobStoragePath),
            () => _provider.DeleteBlob(_account, blobStoragePath),
            () => _provider.DeleteBlobsIfExists(_account, [blobStoragePath]),
        ];

        foreach (Func<Task> operation in operations)
        {
            ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(operation);
            Assert.StartsWith("Blob path escapes the storage account directory", exception.Message);
        }

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _provider.DeleteDataBlobs(_account, "../..", Guid.NewGuid())
        );
        Assert.False(File.Exists(Path.Combine(_root, "escaped")));
    }

    [Fact]
    public async Task EveryOperation_DirectoryOutsideRoot_Throws()
    {
        string sibling = $"{_root}-sibling";
        StorageAccount[] accounts =
        [
            new(8, new FileSystemProviderInfo(sibling)),
            new(9, new FileSystemProviderInfo(Path.Combine(_root, "..", "outside"))),
            new(10, new FileSystemProviderInfo("/tmp")),
        ];

        foreach (StorageAccount account in accounts)
        {
            Func<Task>[] operations =
            [
                () => _provider.WriteBlob(account, new MemoryStream([1]), "ttd/app/blob"),
                () => _provider.ReadBlob(account, "ttd/app/blob"),
                () => _provider.DeleteBlob(account, "ttd/app/blob"),
                () => _provider.DeleteBlobsIfExists(account, ["ttd/app/blob"]),
                () => _provider.DeleteDataBlobs(account, "ttd/app", Guid.NewGuid()),
            ];

            foreach (Func<Task> operation in operations)
            {
                ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(
                    operation
                );
                Assert.Contains("is not under the file-system root directory", exception.Message);
            }
        }

        Assert.False(Directory.Exists(sibling));
    }

    [Fact]
    public async Task EveryOperation_SymlinkedDirectoryPointingOutsideRoot_Throws()
    {
        string outside = CreateOutsideDirectory();
        string linked = Path.Combine(_root, "linked-account");
        Directory.CreateDirectory(_root);
        Directory.CreateSymbolicLink(linked, outside);
        StorageAccount account = new(8, new FileSystemProviderInfo(linked));

        Func<Task>[] operations =
        [
            () => _provider.WriteBlob(account, new MemoryStream([1]), "ttd/app/blob"),
            () => _provider.ReadBlob(account, "ttd/app/blob"),
            () => _provider.DeleteBlob(account, "ttd/app/blob"),
            () => _provider.DeleteBlobsIfExists(account, ["ttd/app/blob"]),
            () => _provider.DeleteDataBlobs(account, "ttd/app", Guid.NewGuid()),
        ];

        foreach (Func<Task> operation in operations)
        {
            ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(operation);
            Assert.Contains("is not under the file-system root directory", exception.Message);
            Assert.Contains($"(resolved: '{outside}'", exception.Message);
        }

        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }

    [Fact]
    public async Task WriteAndReadBlob_SymlinkedSubdirectoryPointingOutsideAccount_Throws()
    {
        string outside = CreateOutsideDirectory();
        await File.WriteAllTextAsync(Path.Combine(outside, "secret"), "secret");
        Directory.CreateDirectory(Path.Combine(_directory, "ttd"));
        Directory.CreateSymbolicLink(Path.Combine(_directory, "ttd", "app"), outside);

        ArgumentException write = await Assert.ThrowsAsync<ArgumentException>(() =>
            _provider.WriteBlob(_account, new MemoryStream([1]), "ttd/app/new/blob")
        );
        ArgumentException read = await Assert.ThrowsAsync<ArgumentException>(() =>
            _provider.ReadBlob(_account, "ttd/app/secret")
        );

        Assert.StartsWith("Blob path escapes the storage account directory", write.Message);
        Assert.Contains($"(resolved: '{Path.Combine(outside, "new", "blob")}')", write.Message);
        Assert.StartsWith("Blob path escapes the storage account directory", read.Message);
        Assert.Contains($"(resolved: '{Path.Combine(outside, "secret")}')", read.Message);
        Assert.Equal([Path.Combine(outside, "secret")], Directory.GetFileSystemEntries(outside));
    }

    [Fact]
    public async Task WriteAndReadBlob_BlobFileIsSymlink_Throws()
    {
        string target = NewBlobPath("data-elements/target");
        string linked = NewBlobPath("data-elements/linked");
        await Write(target, "target");
        string linkedFile = Path.Combine(_directory, linked);
        Directory.CreateDirectory(Path.GetDirectoryName(linkedFile)!);
        File.CreateSymbolicLink(linkedFile, Path.Combine(_directory, target));

        ArgumentException write = await Assert.ThrowsAsync<ArgumentException>(() =>
            _provider.WriteBlob(_account, new MemoryStream([1]), linked)
        );
        ArgumentException read = await Assert.ThrowsAsync<ArgumentException>(() =>
            _provider.ReadBlob(_account, linked)
        );

        Assert.StartsWith("Blob path is a symbolic link", write.Message);
        Assert.StartsWith("Blob path is a symbolic link", read.Message);
        Assert.Equal("target", await File.ReadAllTextAsync(Path.Combine(_directory, target)));
    }

    [Fact]
    public async Task WriteAndReadBlob_SymlinksPointingInsideRoot_Succeed()
    {
        string real = Path.Combine(_root, "real-account");
        string linkedAccount = Path.Combine(_root, "linked-account");
        Directory.CreateDirectory(Path.Combine(real, "ttd", "shared"));
        Directory.CreateSymbolicLink(linkedAccount, real);
        Directory.CreateSymbolicLink(Path.Combine(real, "ttd", "app"), "shared");
        StorageAccount account = new(8, new FileSystemProviderInfo(linkedAccount));

        await _provider.WriteBlob(
            account,
            new MemoryStream(Encoding.UTF8.GetBytes("inside")),
            "ttd/app/blob"
        );
        await using Stream? read = await _provider.ReadBlob(account, "ttd/app/blob");

        Assert.Equal(
            "inside",
            await File.ReadAllTextAsync(Path.Combine(real, "ttd", "shared", "blob"))
        );
        Assert.NotNull(read);
        using StreamReader reader = new(read);
        Assert.Equal("inside", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task WriteBlob_RootDirectoryIsSymlink_WritesThroughIt()
    {
        string realRoot = CreateOutsideDirectory();
        Directory.CreateDirectory(Path.GetDirectoryName(_root)!);
        Directory.CreateSymbolicLink(_root, realRoot);
        string blobStoragePath = NewBlobPath("blob");

        await Write(blobStoragePath, "through the root link");

        Assert.Equal(
            "through the root link",
            await File.ReadAllTextAsync(
                Path.Combine(realRoot, "ttd-dev-appsdata-blob-db", blobStoragePath)
            )
        );
    }

    [Fact]
    public async Task WriteBlob_RootDirectoryNotConfigured_Throws()
    {
        FileSystemBlobProvider provider = CreateProvider(string.Empty);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                provider.WriteBlob(_account, new MemoryStream([1]), NewBlobPath("blob"))
        );

        Assert.Contains("StorageAccounts:FileSystem:RootDirectory", exception.Message);
    }

    [Fact]
    public async Task ReadBlob_AzureAccount_ThrowsNotSupported()
    {
        StorageAccount account = new(null, new AzureBlobProviderInfo("account", "container"));

        NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            _provider.ReadBlob(account, "ttd/app/blob")
        );

        Assert.Equal(
            "Storage account provider is not supported: AzureBlobProviderInfo",
            exception.Message
        );
    }

    private static FileSystemBlobProvider CreateProvider(string rootDirectory)
    {
        return new FileSystemBlobProvider(
            Options.Create(
                new StorageAccountSettings { FileSystem = new() { RootDirectory = rootDirectory } }
            ),
            NullLogger<FileSystemBlobProvider>.Instance
        );
    }

    private static string NewBlobPath(string suffix)
    {
        return $"ttd/app/{Guid.NewGuid()}/{suffix}";
    }

    private async Task Write(string blobStoragePath, string content)
    {
        await _provider.WriteBlob(
            _account,
            new MemoryStream(Encoding.UTF8.GetBytes(content)),
            blobStoragePath
        );
    }
}
