using System;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// A request to add a storage account row.
/// </summary>
public sealed class StorageAccountRequest
{
    /// <summary>
    /// Gets or sets the provider, such as <c>azure-blob</c> or <c>file-system</c>.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Gets or sets the provider info document: the provider's own properties, such as
    /// <c>accountName</c> and <c>container</c> for <c>azure-blob</c>.
    /// </summary>
    public JObject? ProviderInfo { get; set; }

    /// <summary>
    /// Gets or sets the blob storage org the account serves.
    /// </summary>
    public string? Org { get; set; }

    /// <summary>
    /// Gets or sets the application storage account number the account serves, 1 or greater.
    /// </summary>
    public int StorageAccountNumber { get; set; }

    /// <summary>
    /// Gets or sets whether new blob versions for the org and number are written to the account.
    /// Defaults to true, which takes the flag over from the current writable row for the pair.
    /// </summary>
    public bool? Writable { get; set; }

    /// <summary>
    /// Validates the request and reads the provider info document as the type the provider selects.
    /// </summary>
    /// <param name="providerInfo">The provider info, when the request is valid.</param>
    /// <param name="error">Why the request is invalid, when it is.</param>
    /// <returns>Whether the request is valid.</returns>
    public bool TryValidate(
        [NotNullWhen(true)] out StorageAccountProviderInfo? providerInfo,
        [NotNullWhen(false)] out string? error
    )
    {
        providerInfo = null;
        error = null;

        if (string.IsNullOrWhiteSpace(Org))
        {
            error = "Org is required.";
        }
        else if (StorageAccountNumber < 1)
        {
            error = "StorageAccountNumber must be 1 or greater.";
        }
        else if (string.IsNullOrWhiteSpace(Provider))
        {
            error = "Provider is required.";
        }
        else if (ProviderInfo is null)
        {
            error = "ProviderInfo is required.";
        }
        else
        {
            try
            {
                providerInfo = StorageAccountProviderInfo.Deserialize(
                    Provider,
                    ProviderInfo.ToString(Formatting.None)
                );
            }
            catch (NotSupportedException exception)
            {
                error = exception.Message;
            }
            catch (System.Text.Json.JsonException exception)
            {
                error = $"ProviderInfo is not valid for provider '{Provider}': {exception.Message}";
            }
        }

        return error is null;
    }
}
