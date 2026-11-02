using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Configuration;

/// <summary>
/// Settings for resolving storage accounts, bound from the <c>StorageAccounts</c> section.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>StorageAccounts:DefaultProvider</c>: see <see cref="DefaultProvider"/>.</item>
/// <item><c>StorageAccounts:FileSystem:RootDirectory</c>: see <see cref="FileSystemSettings.RootDirectory"/>.</item>
/// </list>
/// </remarks>
public class StorageAccountSettings
{
    /// <summary>
    /// Gets or sets the provider of the pattern default storage account, used for blobs and
    /// (org, number) pairs without a storage account row: <see cref="StorageAccountProviderInfo.AzureBlob"/>
    /// (the default) derives an Azure container from <see cref="AzureStorageConfiguration"/>, and
    /// <see cref="StorageAccountProviderInfo.FileSystem"/> a directory under
    /// <see cref="FileSystemSettings.RootDirectory"/>, which must then be set. Any other value fails
    /// startup (<see cref="StorageAccountSettingsValidator"/>).
    /// </summary>
    public string DefaultProvider { get; set; } = StorageAccountProviderInfo.AzureBlob;

    /// <summary>
    /// Gets or sets the settings of the file-system provider.
    /// </summary>
    public FileSystemSettings FileSystem { get; set; } = new();

    /// <summary>
    /// Settings of the file-system provider, bound from <c>StorageAccounts:FileSystem</c>.
    /// </summary>
    public class FileSystemSettings
    {
        /// <summary>
        /// Gets or sets the directory every file-system storage account must be in. The directory of
        /// a file-system account is this directory or one below it; accounts elsewhere are rejected.
        /// Empty (the default) rejects every file-system account. A relative path is resolved against
        /// the working directory of the process.
        /// </summary>
        public string RootDirectory { get; set; } = string.Empty;
    }
}
