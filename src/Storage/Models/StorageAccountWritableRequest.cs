using System.ComponentModel.DataAnnotations;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// A request to set the writable flag of a storage account row.
/// </summary>
public sealed class StorageAccountWritableRequest
{
    /// <summary>
    /// Gets or sets whether new blob versions for the row's org and number are written to the account.
    /// Setting it takes the flag over from the other writable row for the pair.
    /// </summary>
    [Required]
    public bool? Writable { get; set; }
}
