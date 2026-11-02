using System;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// A blob version read for re-pointing it to another storage account.
/// </summary>
/// <param name="Id">The blob version id.</param>
/// <param name="InstanceGuid">The identifier of the owning instance.</param>
/// <param name="DataElementId">The identifier of the owning data element.</param>
/// <param name="AppId">The application id used as the blob path prefix.</param>
/// <param name="BlobStorageOrg">The org the pattern default is derived from.</param>
/// <param name="StorageAccountNumber">The storage account number the pattern default is derived from, null meaning 1.</param>
/// <param name="StorageAccountId">The id of the storage account holding the blob, or null for the pattern default.</param>
public sealed record BlobVersionRepointRow(
    Guid Id,
    Guid InstanceGuid,
    Guid DataElementId,
    string AppId,
    string BlobStorageOrg,
    int? StorageAccountNumber,
    short? StorageAccountId
);
