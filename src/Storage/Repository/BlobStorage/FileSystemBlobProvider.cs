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
/// every blob path must stay inside its account directory, both as written and with symbolic links
/// resolved; a blob file that is itself a symbolic link is refused too. Anything else is rejected with
/// an <see cref="ArgumentException"/> before a blob is read or written; the check only reads the
/// path segments that exist.
/// </remarks>
public sealed class FileSystemBlobProvider : IBlobStorageProvider
{
    private const int _bufferSize = 81920;

    // Linux's limit on symbolic links followed while resolving one path (MAXSYMLINKS).
    private const int _maxLinks = 40;

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
    public Task<bool> BlobExists(
        StorageAccount account,
        string blobStoragePath,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(File.Exists(ResolveBlob(account, blobStoragePath)));
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
        string notUnderRoot =
            $"Storage account directory '{info.Directory}' is not under the file-system root directory '{_root}'";
        if (!IsAtOrUnder(directory, _root))
        {
            throw new ArgumentException($"{notUnderRoot}.", nameof(account));
        }

        string realRoot = RealPath(_root);
        string realDirectory = RealPath(
            Path.Join(realRoot, Path.GetRelativePath(_root, directory)),
            realRoot
        );
        if (!IsAtOrUnder(realDirectory, realRoot))
        {
            throw new ArgumentException(
                $"{notUnderRoot} (resolved: '{realDirectory}', root resolved: '{realRoot}').",
                nameof(account)
            );
        }

        return realDirectory;
    }

    private string ResolveBlob(StorageAccount account, string blobStoragePath)
    {
        string directory = ResolveDirectory(account);
        string path = Path.GetFullPath(Path.Combine(directory, blobStoragePath.TrimStart('/')));
        if (!IsUnder(path, directory))
        {
            throw new ArgumentException(
                $"Blob path escapes the storage account directory: {blobStoragePath}",
                nameof(blobStoragePath)
            );
        }

        // The directory is already resolved, so only the segments below it are checked for links.
        string realPath = RealPath(path, directory);
        if (!IsUnder(realPath, directory))
        {
            throw new ArgumentException(
                $"Blob path escapes the storage account directory: {blobStoragePath} (resolved: '{realPath}')",
                nameof(blobStoragePath)
            );
        }

        if (new FileInfo(path).LinkTarget is not null)
        {
            throw new ArgumentException(
                $"Blob path is a symbolic link: {blobStoragePath} (resolved: '{realPath}')",
                nameof(blobStoragePath)
            );
        }

        return realPath;
    }

    private static bool IsAtOrUnder(string path, string directory) =>
        string.Equals(path, directory, StringComparison.Ordinal) || IsUnder(path, directory);

    private static bool IsUnder(string path, string directory) =>
        path.StartsWith(
            Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
            StringComparison.Ordinal
        );

    /// <summary>
    /// Resolves the symbolic links in a full path the way <c>realpath</c> does, segment by segment,
    /// because <see cref="Path.GetFullPath(string)"/> does not resolve them. Segments from the first
    /// one that does not exist are appended as they are, so only existing segments are touched.
    /// </summary>
    /// <param name="fullPath">A normalized full path.</param>
    /// <param name="resolvedBase">A resolved ancestor of <paramref name="fullPath"/> to start from; the file-system root when null.</param>
    /// <returns>The path with every existing symbolic link replaced by its target.</returns>
    internal static string RealPath(string fullPath, string? resolvedBase = null)
    {
        string current = resolvedBase ?? Path.GetPathRoot(fullPath)!;
        List<string> pending = [.. Split(Path.GetRelativePath(current, fullPath))];
        bool missing = false;
        int links = 0;
        while (pending.Count > 0)
        {
            string segment = pending[0];
            pending.RemoveAt(0);
            if (segment is "." or "")
            {
                continue;
            }

            if (segment == "..")
            {
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }

            string next = Path.Join(current, segment);
            if (!missing)
            {
                FileInfo info = new(next);
                string? target = info.LinkTarget;
                if (target is not null)
                {
                    if (++links > _maxLinks)
                    {
                        throw new IOException($"Too many levels of symbolic links: {fullPath}");
                    }

                    if (Path.IsPathRooted(target))
                    {
                        current = Path.GetPathRoot(target)!;
                    }

                    pending.InsertRange(0, Split(target));
                    continue;
                }

                missing = (int)info.Attributes == -1;
            }

            current = next;
        }

        return current;
    }

    private static string[] Split(string path) =>
        path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries
        );
}
