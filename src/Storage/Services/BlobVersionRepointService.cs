using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Re-points blob versions after an out-of-band copy of their blobs to another storage account.
/// </summary>
/// <remarks>
/// Blobs are never copied or deleted here. A version is re-pointed only when its blob exists in the
/// target, and only while its row still names the source, so a run can be repeated or resumed at any
/// point and a version whose blob was not copied keeps being read from the source.
/// </remarks>
public sealed class BlobVersionRepointService(
    IBlobVersionRepointRepository repointRepository,
    IStorageAccountRepository storageAccountRepository,
    IBlobRepository blobRepository,
    ILogger<BlobVersionRepointService> logger
) : IBlobVersionRepointService
{
    /// <inheritdoc/>
    public async Task<BlobVersionRepointResult> Repoint(
        BlobVersionRepointJob job,
        CancellationToken cancellationToken = default
    )
    {
        StorageAccountRows targetRows = await storageAccountRepository.ReadOne(
            job.ToAccountId,
            cancellationToken
        );
        if (targetRows.Unreadable.SingleOrDefault() is UnreadableStorageAccountRow unreadable)
        {
            return BlobVersionRepointResult.Rejected(unreadable.Description);
        }

        StorageAccountRow? target = targetRows.Readable.SingleOrDefault();
        if (target is null)
        {
            return BlobVersionRepointResult.Rejected(
                $"Storage account {job.ToAccountId} does not exist."
            );
        }

        string org;
        int number;
        if (job.FromAccountId is short fromAccountId)
        {
            // Only the org and number of the source are used, so a source this build cannot read
            // can still be repointed away from.
            StorageAccountRows sourceRows = await storageAccountRepository.ReadOne(
                fromAccountId,
                cancellationToken
            );
            (string Org, int Number)? source = sourceRows
                .Readable.Select(row => (row.Org, row.StorageAccountNumber))
                .Concat(sourceRows.Unreadable.Select(row => (row.Org, row.StorageAccountNumber)))
                .Cast<(string, int)?>()
                .SingleOrDefault();
            if (source is null)
            {
                return BlobVersionRepointResult.Rejected(
                    $"Storage account {fromAccountId} does not exist."
                );
            }

            (org, number) = source.Value;
        }
        else
        {
            (org, number) = (job.Org!, job.StorageAccountNumber!.Value);
        }

        // An account row serves one org and number; versions of another pair would be recorded on an
        // account that write routing and the account listing attribute to a different owner.
        if (target.Org != org || target.StorageAccountNumber != number)
        {
            return BlobVersionRepointResult.Rejected(
                $"Storage account {target.Id} serves org '{target.Org}' number {target.StorageAccountNumber}, not org '{org}' number {number}."
            );
        }

        StorageAccount targetAccount = new(target.Id, target.ProviderInfo);
        return BlobVersionRepointResult.Ran(
            await Run(job, org, number, targetAccount, cancellationToken)
        );
    }

    private async Task<BlobVersionRepointSummary> Run(
        BlobVersionRepointJob job,
        string org,
        int number,
        StorageAccount target,
        CancellationToken cancellationToken
    )
    {
        int examined = 0;
        int repointed = 0;
        int missingInTarget = 0;
        int alreadyMoved = 0;
        List<Guid> missingIds = [];
        Guid? lastId = job.AfterId;
        bool completed = false;

        for (int batch = 0; batch < job.MaxBatches && !completed; batch++)
        {
            IReadOnlyList<BlobVersionRepointRow> rows = await repointRepository.ReadPage(
                job.FromAccountId,
                job.FromAccountId is null ? org : null,
                job.FromAccountId is null ? number : null,
                job.AppId,
                lastId ?? Guid.Empty,
                job.BatchSize,
                cancellationToken
            );
            completed = rows.Count < job.BatchSize;

            foreach (BlobVersionRepointRow row in rows)
            {
                examined++;
                lastId = row.Id;
                string blobStoragePath = DataElementHelper.GetVersionedBlobPath(
                    row.AppId,
                    row.InstanceGuid,
                    BlobVersionId.Encode(row.Id)
                );

                if (!await blobRepository.BlobExists(target, blobStoragePath, cancellationToken))
                {
                    missingInTarget++;
                    if (missingIds.Count < BlobVersionRepointSummary.MaxListedMissingIds)
                    {
                        missingIds.Add(row.Id);
                    }

                    logger.LogWarning(
                        "Blob version {BlobVersionId} of data element {DataElementId} is not in storage account {StorageAccountId} at {BlobStoragePath}; left on its current account.",
                        row.Id,
                        row.DataElementId,
                        target.Id,
                        blobStoragePath
                    );
                    continue;
                }

                if (job.DryRun)
                {
                    repointed++;
                }
                else if (
                    await repointRepository.UpdateAccount(
                        row.Id,
                        job.FromAccountId,
                        target.Id!.Value,
                        cancellationToken
                    )
                )
                {
                    repointed++;
                }
                else
                {
                    alreadyMoved++;
                }
            }
        }

        BlobVersionRepointSummary summary = new()
        {
            DryRun = job.DryRun,
            Examined = examined,
            Repointed = repointed,
            MissingInTarget = missingInTarget,
            MissingInTargetIds = missingIds,
            AlreadyMoved = alreadyMoved,
            LastId = lastId,
            Completed = completed,
        };

        logger.LogInformation(
            "Re-point to storage account {StorageAccountId} from {FromAccountId} (org {Org}, number {Number}, app {AppId}, dry run {DryRun}): examined {Examined}, re-pointed {Repointed}, missing in target {MissingInTarget}, already moved {AlreadyMoved}, last id {LastId}, completed {Completed}.",
            target.Id,
            job.FromAccountId?.ToString() ?? "pattern default",
            org,
            number,
            job.AppId,
            job.DryRun,
            examined,
            repointed,
            missingInTarget,
            alreadyMoved,
            lastId,
            completed
        );

        return summary;
    }
}
