using Altinn.Platform.Storage.Models;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Configuration;

/// <summary>
/// Validates <see cref="StorageAccountSettings"/> when the application starts: the default provider
/// must be known, and the file-system provider needs a root directory.
/// </summary>
public sealed class StorageAccountSettingsValidator : IValidateOptions<StorageAccountSettings>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, StorageAccountSettings options)
    {
        return options.DefaultProvider switch
        {
            StorageAccountProviderInfo.AzureBlob => ValidateOptionsResult.Success,
            StorageAccountProviderInfo.FileSystem
                when string.IsNullOrWhiteSpace(options.FileSystem?.RootDirectory) =>
                ValidateOptionsResult.Fail(
                    $"StorageAccounts:FileSystem:RootDirectory must be set when StorageAccounts:DefaultProvider is '{StorageAccountProviderInfo.FileSystem}'."
                ),
            StorageAccountProviderInfo.FileSystem => ValidateOptionsResult.Success,
            _ => ValidateOptionsResult.Fail(
                $"StorageAccounts:DefaultProvider '{options.DefaultProvider}' is not supported; use '{StorageAccountProviderInfo.AzureBlob}' or '{StorageAccountProviderInfo.FileSystem}'."
            ),
        };
    }
}
