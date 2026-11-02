using System;
using System.Diagnostics.CodeAnalysis;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// A request to re-point blob versions from one storage account to another, after their blobs have
/// been copied to the target under the same paths. The source is either <c>fromAccountId</c>
/// or the pattern default of <c>org</c> and <c>storageAccountNumber</c>.
/// </summary>
public sealed class BlobVersionRepointRequest
{
    /// <summary>
    /// The number of versions read per page when <see cref="BatchSize"/> is not set.
    /// </summary>
    public const int DefaultBatchSize = 100;

    /// <summary>
    /// The largest allowed <see cref="BatchSize"/>.
    /// </summary>
    public const int MaxBatchSize = 1000;

    /// <summary>
    /// The number of pages per run when <see cref="MaxBatches"/> is not set.
    /// </summary>
    public const int DefaultMaxBatches = 10;

    /// <summary>
    /// The largest allowed <see cref="MaxBatches"/>.
    /// </summary>
    public const int MaxMaxBatches = 100;

    /// <summary>
    /// Gets or sets the storage account the versions are recorded on. Leave it out to select the
    /// versions on the pattern default with <c>org</c> and <c>storageAccountNumber</c>.
    /// </summary>
    public short? FromAccountId { get; set; }

    /// <summary>
    /// Gets or sets the blob storage org of the pattern default the versions are on.
    /// </summary>
    public string? Org { get; set; }

    /// <summary>
    /// Gets or sets the storage account number of the pattern default the versions are on, 1 or greater.
    /// Versions recorded without a number count as 1.
    /// </summary>
    public int? StorageAccountNumber { get; set; }

    /// <summary>
    /// Gets or sets the storage account the versions are re-pointed to.
    /// </summary>
    public short? ToAccountId { get; set; }

    /// <summary>
    /// Gets or sets the application whose versions are re-pointed, or null for all of them.
    /// </summary>
    public string? AppId { get; set; }

    /// <summary>
    /// Gets or sets the number of versions read per page, 1 to 1000.
    /// </summary>
    public int? BatchSize { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of pages in this run, 1 to 100.
    /// </summary>
    public int? MaxBatches { get; set; }

    /// <summary>
    /// Gets or sets whether to only count, without changing any row. Defaults to false.
    /// </summary>
    public bool? DryRun { get; set; }

    /// <summary>
    /// Gets or sets the version id to continue after: the <c>lastId</c> of the previous run.
    /// </summary>
    public Guid? AfterId { get; set; }

    /// <summary>
    /// Validates the request and applies the defaults.
    /// </summary>
    /// <param name="job">The run, when the request is valid.</param>
    /// <param name="error">Why the request is invalid, when it is.</param>
    /// <returns>Whether the request is valid.</returns>
    public bool TryValidate(
        [NotNullWhen(true)] out BlobVersionRepointJob? job,
        [NotNullWhen(false)] out string? error
    )
    {
        job = null;
        error = Validate();
        if (error is not null)
        {
            return false;
        }

        job = new BlobVersionRepointJob(
            FromAccountId,
            FromAccountId is null ? Org : null,
            FromAccountId is null ? StorageAccountNumber : null,
            ToAccountId!.Value,
            string.IsNullOrWhiteSpace(AppId) ? null : AppId,
            BatchSize ?? DefaultBatchSize,
            MaxBatches ?? DefaultMaxBatches,
            DryRun ?? false,
            AfterId
        );
        return true;
    }

    private string? Validate()
    {
        bool hasPattern = Org is not null || StorageAccountNumber is not null;
        if (ToAccountId is null)
        {
            return "ToAccountId is required.";
        }

        if (FromAccountId is not null && hasPattern)
        {
            return "Set either FromAccountId or Org and StorageAccountNumber, not both.";
        }

        if (FromAccountId is null)
        {
            if (string.IsNullOrWhiteSpace(Org) || StorageAccountNumber is null)
            {
                return "Set either FromAccountId or Org and StorageAccountNumber.";
            }

            if (StorageAccountNumber < 1)
            {
                return "StorageAccountNumber must be 1 or greater.";
            }
        }
        else if (FromAccountId == ToAccountId)
        {
            return "FromAccountId and ToAccountId must differ.";
        }

        if (BatchSize is < 1 or > MaxBatchSize)
        {
            return $"BatchSize must be between 1 and {MaxBatchSize}.";
        }

        if (MaxBatches is < 1 or > MaxMaxBatches)
        {
            return $"MaxBatches must be between 1 and {MaxMaxBatches}.";
        }

        return null;
    }
}
