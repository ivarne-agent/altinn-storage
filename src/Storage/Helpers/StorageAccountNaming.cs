using System;
using System.IO;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;

namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// Derives the storage account of a service owner from the configured naming pattern, used for
/// blobs that have no storage account row.
/// </summary>
public static class StorageAccountNaming
{
    /// <summary>
    /// The storage account number used when an application does not set one.
    /// </summary>
    public const int DefaultStorageAccountNumber = 1;

    /// <summary>
    /// Gets the storage account number of an application.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The application's storage account number, or the default number.</returns>
    public static int NumberOf(Application app)
    {
        return app.StorageAccountNumber ?? DefaultStorageAccountNumber;
    }

    /// <summary>
    /// Gets the storage account that the naming pattern of the configured default provider gives for an
    /// org and storage account number.
    /// </summary>
    /// <param name="config">The storage configuration holding the naming pattern.</param>
    /// <param name="settings">The storage account settings selecting the default provider.</param>
    /// <param name="org">The blob storage org.</param>
    /// <param name="number">The storage account number.</param>
    /// <returns>The provider info of the account.</returns>
    /// <exception cref="NotSupportedException">The default provider is unknown.</exception>
    public static StorageAccountProviderInfo PatternDefault(
        AzureStorageConfiguration config,
        StorageAccountSettings settings,
        string org,
        int number
    )
    {
        return settings.DefaultProvider switch
        {
            StorageAccountProviderInfo.AzureBlob => PatternDefault(config, org, number),
            StorageAccountProviderInfo.FileSystem => FileSystemPatternDefault(
                config,
                settings.FileSystem.RootDirectory,
                org,
                number
            ),
            _ => throw new NotSupportedException(
                $"Storage account provider '{settings.DefaultProvider}' is not supported."
            ),
        };
    }

    /// <summary>
    /// Gets the directory that the naming pattern gives for an org and storage account number.
    /// </summary>
    /// <param name="config">The storage configuration holding the container naming pattern.</param>
    /// <param name="rootDirectory">The root directory of file-system storage accounts.</param>
    /// <param name="org">The blob storage org.</param>
    /// <param name="number">The storage account number.</param>
    /// <returns>The provider info of the directory.</returns>
    /// <remarks>
    /// The folder is <see cref="AzureStorageConfiguration.OrgStorageContainer"/> formatted with the org,
    /// with <c>-NN</c> appended when the number is not the default number, so each account the Azure
    /// pattern distinguishes gets its own folder.
    /// </remarks>
    public static FileSystemProviderInfo FileSystemPatternDefault(
        AzureStorageConfiguration config,
        string rootDirectory,
        string org,
        int number
    )
    {
        string folder = string.Format(config.OrgStorageContainer, org);
        if (number != DefaultStorageAccountNumber)
        {
            folder = $"{folder}-{number:D2}";
        }

        return new FileSystemProviderInfo(Path.Combine(rootDirectory, folder));
    }

    /// <summary>
    /// Gets the Azure blob container that the naming pattern gives for an org and storage account number.
    /// </summary>
    /// <param name="config">The storage configuration holding the naming pattern.</param>
    /// <param name="org">The blob storage org.</param>
    /// <param name="number">The storage account number.</param>
    /// <returns>The provider info of the container.</returns>
    /// <remarks>
    /// The account name is <see cref="AzureStorageConfiguration.OrgStorageAccount"/> formatted with the org,
    /// with its last two characters replaced by the number when it is not the default number. Azurite has
    /// a single account, so the number does not apply there. Both names are lowercased, since Azure
    /// account and container names are lowercase.
    /// </remarks>
    public static AzureBlobProviderInfo PatternDefault(
        AzureStorageConfiguration config,
        string org,
        int number
    )
    {
        string container = string.Format(config.OrgStorageContainer, org).ToLowerInvariant();
        if (config.AccountName == AzureBlobLocation.AzuriteAccountName)
        {
            return new AzureBlobProviderInfo(config.AccountName, container);
        }

        string accountName = string.Format(config.OrgStorageAccount, org).ToLowerInvariant();
        if (number != DefaultStorageAccountNumber)
        {
            accountName = $"{accountName.AsSpan(0, accountName.Length - 2)}{number:D2}";
        }

        return new AzureBlobProviderInfo(accountName, container);
    }
}
