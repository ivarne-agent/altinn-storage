using System;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Thrown when making a storage account writable collides with a concurrent hand-over of the
/// writable flag for the same org and storage account number. Nothing was changed; the caller can retry.
/// </summary>
/// <param name="org">The blob storage org of the pair.</param>
/// <param name="storageAccountNumber">The storage account number of the pair.</param>
/// <param name="innerException">The unique violation the database reported.</param>
public sealed class StorageAccountWritableConflictException(
    string org,
    int storageAccountNumber,
    Exception innerException
)
    : Exception(
        $"Another writable storage account for org '{org}' number {storageAccountNumber} was set at the same time; retry.",
        innerException
    )
{
    /// <summary>
    /// The blob storage org of the pair.
    /// </summary>
    public string Org { get; } = org;

    /// <summary>
    /// The storage account number of the pair.
    /// </summary>
    public int StorageAccountNumber { get; } = storageAccountNumber;
}
