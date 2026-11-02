using System;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// One run of re-pointing blob versions from one storage account to another, after their blobs have
/// been copied to the target under the same paths.
/// </summary>
/// <param name="FromAccountId">
/// The storage account the versions are recorded on, or null for the pattern default of
/// <paramref name="Org"/> and <paramref name="StorageAccountNumber"/>.
/// </param>
/// <param name="Org">The blob storage org of the pattern default, when <paramref name="FromAccountId"/> is null.</param>
/// <param name="StorageAccountNumber">The storage account number of the pattern default, when <paramref name="FromAccountId"/> is null.</param>
/// <param name="ToAccountId">The storage account the versions are re-pointed to.</param>
/// <param name="AppId">Only versions of this application, or null for all.</param>
/// <param name="BatchSize">The number of versions read per page.</param>
/// <param name="MaxBatches">The maximum number of pages in this run.</param>
/// <param name="DryRun">Whether to only count, without changing any row.</param>
/// <param name="AfterId">The run starts after this version id, or at the beginning when null.</param>
public sealed record BlobVersionRepointJob(
    short? FromAccountId,
    string? Org,
    int? StorageAccountNumber,
    short ToAccountId,
    string? AppId,
    int BatchSize,
    int MaxBatches,
    bool DryRun,
    Guid? AfterId
);
