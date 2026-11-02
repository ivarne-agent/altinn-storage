using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Re-points blob versions from one storage account to another once their blobs have been copied there.
/// </summary>
public interface IBlobVersionRepointService
{
    /// <summary>
    /// Runs one slice of a re-point: reads up to <see cref="BlobVersionRepointJob.MaxBatches"/> pages of
    /// versions on the source account and records the target account on each version whose blob exists
    /// in the target. Versions whose blob is missing in the target are left on the source.
    /// </summary>
    /// <param name="job">The run.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The summary, or why the run could not start.</returns>
    Task<BlobVersionRepointResult> Repoint(
        BlobVersionRepointJob job,
        CancellationToken cancellationToken = default
    );
}
