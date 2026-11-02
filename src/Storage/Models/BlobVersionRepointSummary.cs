using System;
using System.Collections.Generic;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// The outcome of one run of re-pointing blob versions.
/// </summary>
public sealed record BlobVersionRepointSummary
{
    /// <summary>
    /// The maximum number of ids listed in <see cref="MissingInTargetIds"/>.
    /// </summary>
    public const int MaxListedMissingIds = 100;

    /// <summary>
    /// Gets a value indicating whether the run only counted, without changing any row.
    /// </summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Gets the number of versions read.
    /// </summary>
    public int Examined { get; init; }

    /// <summary>
    /// Gets the number of versions re-pointed, or in a dry run the number that would have been.
    /// </summary>
    public int Repointed { get; init; }

    /// <summary>
    /// Gets the number of versions left in place because their blob is not in the target account.
    /// </summary>
    public int MissingInTarget { get; init; }

    /// <summary>
    /// Gets the ids of the first versions counted in <c>missingInTarget</c>, at most
    /// 100.
    /// </summary>
    public IReadOnlyList<Guid> MissingInTargetIds { get; init; } = [];

    /// <summary>
    /// Gets the number of versions that no longer named the source account when they were updated.
    /// </summary>
    public int AlreadyMoved { get; init; }

    /// <summary>
    /// Gets the id of the last version read, or the requested <c>afterId</c> when none was read; pass
    /// it as <c>afterId</c> to continue.
    /// </summary>
    public Guid? LastId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the run reached the last version on the source account.
    /// </summary>
    public bool Completed { get; init; }
}
