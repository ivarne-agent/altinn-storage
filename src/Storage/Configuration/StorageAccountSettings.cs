using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Configuration;

/// <summary>
/// Settings for resolving storage accounts, bound from the <c>StorageAccounts</c> section.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>StorageAccounts:DefaultProvider</c>: see <see cref="DefaultProvider"/>.</item>
/// <item><c>StorageAccounts:FileSystem:RootDirectory</c>: see <see cref="FileSystemSettings.RootDirectory"/>.</item>
/// <item><c>StorageAccounts:S3:ServiceUrl</c>: see <see cref="S3Settings.ServiceUrl"/>.</item>
/// <item><c>StorageAccounts:S3:AccessKey</c> and <c>StorageAccounts:S3:SecretKey</c>: see <see cref="S3Settings.AccessKey"/>.</item>
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
    /// startup (<see cref="StorageAccountSettingsValidator"/>), including
    /// <see cref="StorageAccountProviderInfo.S3"/>: the pattern has no S3 bucket naming, so S3 accounts
    /// exist only as explicit storage account rows.
    /// </summary>
    public string DefaultProvider { get; set; } = StorageAccountProviderInfo.AzureBlob;

    /// <summary>
    /// Gets or sets the settings of the file-system provider.
    /// </summary>
    public FileSystemSettings FileSystem { get; set; } = new();

    /// <summary>
    /// Gets or sets the settings of the S3 provider.
    /// </summary>
    public S3Settings S3 { get; set; } = new();

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

    /// <summary>
    /// Settings of the S3 provider, bound from <c>StorageAccounts:S3</c>. The endpoint and the
    /// credentials are configuration only: a storage account row names a bucket and a region, never a
    /// host or a key, so a row cannot direct the credentials of the process at another host.
    /// </summary>
    public class S3Settings
    {
        /// <summary>
        /// Gets or sets the endpoint of an S3-compatible service, such as <c>http://localhost:9000</c>
        /// for MinIO, addressed path-style. Empty (the default) uses AWS S3 in the region of each
        /// account. When set, it must be an absolute <c>http</c> or <c>https</c> URI.
        /// </summary>
        public string ServiceUrl { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the access key id. Set together with <see cref="SecretKey"/>; both empty (the
        /// default) uses the AWS default credential chain (environment, profile, web identity or
        /// instance role).
        /// </summary>
        public string AccessKey { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the secret access key belonging to <see cref="AccessKey"/>.
        /// </summary>
        public string SecretKey { get; set; } = string.Empty;
    }
}
