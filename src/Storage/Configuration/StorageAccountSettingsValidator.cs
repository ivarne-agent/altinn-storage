using System;
using System.Collections.Generic;
using Altinn.Platform.Storage.Models;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Configuration;

/// <summary>
/// Validates <see cref="StorageAccountSettings"/> when the application starts: the default provider
/// must be <see cref="StorageAccountProviderInfo.AzureBlob"/> or
/// <see cref="StorageAccountProviderInfo.FileSystem"/> (never <see cref="StorageAccountProviderInfo.S3"/>,
/// which has no pattern default), the file-system provider needs a root directory, and the S3
/// settings must be a usable endpoint and a complete key pair.
/// </summary>
public sealed class StorageAccountSettingsValidator : IValidateOptions<StorageAccountSettings>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, StorageAccountSettings options)
    {
        List<string> failures = [];

        string? defaultProviderFailure = options.DefaultProvider switch
        {
            StorageAccountProviderInfo.AzureBlob => null,
            StorageAccountProviderInfo.FileSystem
                when string.IsNullOrWhiteSpace(options.FileSystem?.RootDirectory) =>
                $"StorageAccounts:FileSystem:RootDirectory must be set when StorageAccounts:DefaultProvider is '{StorageAccountProviderInfo.FileSystem}'.",
            StorageAccountProviderInfo.FileSystem => null,
            _ =>
                $"StorageAccounts:DefaultProvider '{options.DefaultProvider}' is not supported; use '{StorageAccountProviderInfo.AzureBlob}' or '{StorageAccountProviderInfo.FileSystem}' ('{StorageAccountProviderInfo.S3}' accounts are explicit storage account rows only).",
        };
        if (defaultProviderFailure is not null)
        {
            failures.Add(defaultProviderFailure);
        }

        StorageAccountSettings.S3Settings s3 = options.S3 ?? new();
        if (
            !string.IsNullOrEmpty(s3.ServiceUrl)
            && !(
                Uri.TryCreate(s3.ServiceUrl, UriKind.Absolute, out Uri? serviceUrl)
                && (
                    serviceUrl.Scheme == Uri.UriSchemeHttp
                    || serviceUrl.Scheme == Uri.UriSchemeHttps
                )
            )
        )
        {
            failures.Add(
                $"StorageAccounts:S3:ServiceUrl '{s3.ServiceUrl}' must be an absolute http or https URI, or empty for AWS S3."
            );
        }

        if (string.IsNullOrEmpty(s3.AccessKey) != string.IsNullOrEmpty(s3.SecretKey))
        {
            failures.Add(
                "StorageAccounts:S3:AccessKey and StorageAccounts:S3:SecretKey must both be set, or both be empty to use the AWS default credential chain."
            );
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
