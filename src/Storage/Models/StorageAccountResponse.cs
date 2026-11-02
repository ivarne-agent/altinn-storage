using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// A storage account row. A row whose provider info this build cannot read has no provider info
/// and says why in its error.
/// </summary>
public sealed class StorageAccountResponse
{
    private static readonly JsonSerializerSettings _documentSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
    };

    /// <summary>
    /// Gets the row id, recorded on blob versions written to the account.
    /// </summary>
    public required short Id { get; init; }

    /// <summary>
    /// Gets the provider, such as <c>azure-blob</c> or <c>file-system</c>.
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Gets the provider info document: the provider's own properties. Null when this build cannot
    /// read it.
    /// </summary>
    public JObject? ProviderInfo { get; init; }

    /// <summary>
    /// Gets the blob storage org the account serves.
    /// </summary>
    public required string Org { get; init; }

    /// <summary>
    /// Gets the application storage account number the account serves.
    /// </summary>
    public required int StorageAccountNumber { get; init; }

    /// <summary>
    /// Gets whether new blob versions for the org and number are written to the account.
    /// </summary>
    public required bool Writable { get; init; }

    /// <summary>
    /// Gets when the row was created.
    /// </summary>
    public required DateTimeOffset Created { get; init; }

    /// <summary>
    /// Gets why this build cannot read the provider info, or null when it can. Blob operations in
    /// the account, and writes for its org and number while it is writable, fail until the row is fixed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Creates the response for a row.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The response.</returns>
    public static StorageAccountResponse From(StorageAccountRow row) =>
        new()
        {
            Id = row.Id,
            Provider = row.Provider,
            ProviderInfo = JsonConvert.DeserializeObject<JObject>(
                row.ProviderInfo.Serialize(),
                _documentSettings
            )!,
            Org = row.Org,
            StorageAccountNumber = row.StorageAccountNumber,
            Writable = row.Writable,
            Created = row.Created,
        };

    /// <summary>
    /// Creates the response for a row whose provider info this build cannot read.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The response.</returns>
    public static StorageAccountResponse From(UnreadableStorageAccountRow row) =>
        new()
        {
            Id = row.Id,
            Provider = row.Provider,
            Org = row.Org,
            StorageAccountNumber = row.StorageAccountNumber,
            Writable = row.Writable,
            Created = row.Created,
            Error = row.Error,
        };

    /// <summary>
    /// Creates the responses for rows, ordered by id.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The responses.</returns>
    public static List<StorageAccountResponse> From(StorageAccountRows rows) =>
        [
            .. rows
                .Readable.Select(From)
                .Concat(rows.Unreadable.Select(From))
                .OrderBy(response => response.Id),
        ];
}
