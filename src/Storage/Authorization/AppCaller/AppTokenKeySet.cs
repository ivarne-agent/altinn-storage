using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Platform.Storage.Authorization.AppCaller;

/// <summary>
/// In-memory set of app token signing keys. The whole set is replaced at once, so a lookup
/// always sees one consistent snapshot and never calls Key Vault.
/// </summary>
/// <param name="timeProvider">The time provider.</param>
public sealed class AppTokenKeySet(TimeProvider timeProvider) : IAppTokenSigningKeyProvider
{
    private Snapshot? _current;

    /// <inheritdoc/>
    public TimeSpan? KeySetAge =>
        Volatile.Read(ref _current) is { } snapshot
            ? timeProvider.GetUtcNow() - snapshot.LoadedAt
            : null;

    /// <summary>
    /// Gets the number of key versions in the current set.
    /// </summary>
    public int Count => Volatile.Read(ref _current)?.Count ?? 0;

    /// <inheritdoc/>
    public bool TryGetKey(string org, string kid, [NotNullWhen(true)] out SecurityKey? key)
    {
        key = null;
        Snapshot? snapshot = Volatile.Read(ref _current);
        return snapshot is not null
            && snapshot.Keys.TryGetValue(org, out FrozenDictionary<string, SecurityKey>? versions)
            && versions.TryGetValue(kid, out key);
    }

    /// <summary>
    /// Replaces the whole key set.
    /// </summary>
    /// <param name="keys">Every usable key version.</param>
    public void Replace(IEnumerable<AppTokenSigningKey> keys)
    {
        Dictionary<string, Dictionary<string, SecurityKey>> byOrg = new(
            StringComparer.OrdinalIgnoreCase
        );
        int count = 0;
        foreach (AppTokenSigningKey key in keys)
        {
            if (!byOrg.TryGetValue(key.Org, out Dictionary<string, SecurityKey>? versions))
            {
                versions = new Dictionary<string, SecurityKey>(StringComparer.Ordinal);
                byOrg[key.Org] = versions;
            }

            if (versions.TryAdd(key.Kid, key.Key))
            {
                count++;
            }
        }

        FrozenDictionary<string, FrozenDictionary<string, SecurityKey>> frozen =
            byOrg.ToFrozenDictionary(
                entry => entry.Key,
                entry => entry.Value.ToFrozenDictionary(StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase
            );

        Interlocked.Exchange(ref _current, new Snapshot(frozen, timeProvider.GetUtcNow(), count));
    }

    private sealed record Snapshot(
        FrozenDictionary<string, FrozenDictionary<string, SecurityKey>> Keys,
        DateTimeOffset LoadedAt,
        int Count
    );
}
