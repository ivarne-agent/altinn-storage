using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Repository.BlobStorage;

/// <summary>
/// Stores blobs as files. A blob is the file <c>{Directory}/{blobStoragePath}</c> of the account's
/// <see cref="FileSystemProviderInfo"/>, so a directory mirrors the layout of an Azure container.
/// </summary>
/// <remarks>
/// Every account directory must be <c>StorageAccounts:FileSystem:RootDirectory</c> or below it, and
/// every blob path must stay inside its account directory; anything else is rejected with an
/// <see cref="ArgumentException"/> before the file system is touched.
/// </remarks>
public sealed class FileSystemBlobProvider : IBlobStorageProvider
{
    private const int _bufferSize = 81920;

    private readonly string? _root;
    private readonly ILogger<FileSystemBlobProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemBlobProvider"/> class.
    /// </summary>
    /// <param name="settings">The storage account settings holding the root directory.</param>
    /// <param name="logger">The logger.</param>
    public FileSystemBlobProvider(
        IOptions<StorageAccountSettings> settings,
        ILogger<FileSystemBlobProvider> logger
    )
    {
        string rootDirectory = settings.Value.FileSystem.RootDirectory;
        _root = string.IsNullOrWhiteSpace(rootDirectory)
            ? null
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Provider => StorageAccountProviderInfo.FileSystem;

    /// <inheritdoc/>
    public async Task<(long ContentLength, DateTimeOffset LastModified)> WriteBlob(
        StorageAccount account,
        Stream stream,
        string blobStoragePath
    )
    {
        string path = ResolveBlob(account, blobStoragePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Readers never see a partly written blob: the content is written next to the target and
        // moved over it in one rename.
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (
                FileStream file = new(
                    temp,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    _bufferSize,
                    useAsync: true
                )
            )
            {
                await stream.CopyToAsync(file);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        FileInfo info = new(path);
        return (info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
    }

    /// <inheritdoc/>
    public Task<Stream?> ReadBlob(
        StorageAccount account,
        string blobStoragePath,
        CancellationToken cancellationToken = default
    )
    {
        string path = ResolveBlob(account, blobStoragePath);
        try
        {
            // FileShare.Delete lets a concurrent overwrite or delete proceed while the blob is read.
            Stream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                _bufferSize,
                useAsync: true
            );
            return Task.FromResult<Stream?>(stream);
        }
        catch (Exception exception)
            when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            _logger.LogWarning(
                "Unable to find a blob based on the given information - {Directory}: {BlobStoragePath}",
                GetInfo(account).Directory,
                blobStoragePath
            );
            return Task.FromResult<Stream?>(null);
        }
    }

    /// <inheritdoc/>
    public Task<bool> DeleteBlob(StorageAccount account, string blobStoragePath)
    {
        return Task.FromResult(DeleteIfExists(ResolveBlob(account, blobStoragePath)));
    }

    /// <inheritdoc/>
    public Task<bool[]> DeleteBlobsIfExists(
        StorageAccount account,
        IReadOnlyList<string> blobStoragePaths,
        CancellationToken cancellationToken = default
    )
    {
        blobStoragePaths ??= [];
        bool[] results = new bool[blobStoragePaths.Count];
        for (int index = 0; index < blobStoragePaths.Count; index++)
        {
            string blobStoragePath = blobStoragePaths[index];
            if (string.IsNullOrEmpty(blobStoragePath))
            {
                continue;
            }

            // True means deleted or already missing, as for the Azure batch delete (202 or 404).
            DeleteIfExists(ResolveBlob(account, blobStoragePath));
            results[index] = true;
        }

        return Task.FromResult(results);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteDataBlobs(
        StorageAccount account,
        string appId,
        Guid instanceGuid,
        CancellationToken cancellationToken = default
    )
    {
        string folder = ResolveBlob(account, $"{appId}/{instanceGuid}");
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(
                exception,
                "BlobService // DeleteDataBlobs // Directory: {Directory}",
                folder
            );
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A directory has no URL of its own, so blobs in a file-system account cannot be read through one.
    /// </remarks>
    public Task<(Uri Url, DateTimeOffset ExpiresOn)> GetReadUrl(
        StorageAccount account,
        string blobStoragePath,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default
    ) =>
        throw new NotSupportedException(
            "Blobs in a file-system storage account cannot be read through a URL."
        );

    private static bool DeleteIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private static FileSystemProviderInfo GetInfo(StorageAccount account)
    {
        return account.ProviderInfo as FileSystemProviderInfo
            ?? throw new NotSupportedException(
                $"Storage account provider is not supported: {account.ProviderInfo?.GetType().Name}"
            );
    }

    private string ResolveDirectory(StorageAccount account)
    {
        FileSystemProviderInfo info = GetInfo(account);
        if (_root is null)
        {
            throw new InvalidOperationException(
                $"Storage account {account.Id} uses the file system, but StorageAccounts:FileSystem:RootDirectory is not configured."
            );
        }

        string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(info.Directory));
        if (
            !string.Equals(directory, _root, StringComparison.Ordinal)
            && !directory.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        )
        {
            throw new ArgumentException(
                $"Storage account directory '{info.Directory}' is not under the file-system root directory '{_root}'.",
                nameof(account)
            );
        }

        return directory;
    }

    private string ResolveBlob(StorageAccount account, string blobStoragePath)
    {
        string directory = ResolveDirectory(account);
        string path = Path.GetFullPath(Path.Combine(directory, blobStoragePath.TrimStart('/')));
        if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Blob path escapes the storage account directory: {blobStoragePath}",
                nameof(blobStoragePath)
            );
        }

        return path;
    }
}
