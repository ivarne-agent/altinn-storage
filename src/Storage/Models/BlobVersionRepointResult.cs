namespace Altinn.Platform.Storage.Models;

/// <summary>
/// The result of a request to re-point blob versions: a summary, or why the run could not start.
/// </summary>
/// <param name="Summary">The summary, when the run started.</param>
/// <param name="Error">Why the run could not start, when it did not.</param>
public sealed record BlobVersionRepointResult(BlobVersionRepointSummary? Summary, string? Error)
{
    /// <summary>
    /// Creates a result for a run that took place.
    /// </summary>
    /// <param name="summary">The summary.</param>
    /// <returns>The result.</returns>
    public static BlobVersionRepointResult Ran(BlobVersionRepointSummary summary) =>
        new(summary, null);

    /// <summary>
    /// Creates a result for a run that could not start.
    /// </summary>
    /// <param name="error">Why the run could not start.</param>
    /// <returns>The result.</returns>
    public static BlobVersionRepointResult Rejected(string error) => new(null, error);
}
