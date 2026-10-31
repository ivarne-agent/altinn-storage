using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Platform.Storage.Authorization.AppCaller;

/// <summary>
/// Looks up the public keys that app tokens are signed with.
/// </summary>
public interface IAppTokenSigningKeyProvider
{
    /// <summary>
    /// Gets the time since the key set was last loaded, or null if it has never been loaded.
    /// </summary>
    TimeSpan? KeySetAge { get; }

    /// <summary>
    /// Gets the key for a key version that belongs to an org.
    /// </summary>
    /// <param name="org">The org code.</param>
    /// <param name="kid">The Key Vault key version.</param>
    /// <param name="key">The key, when found.</param>
    /// <returns>True if the org has a key with that version.</returns>
    bool TryGetKey(string org, string kid, [NotNullWhen(true)] out SecurityKey? key);
}
