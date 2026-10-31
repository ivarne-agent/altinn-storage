using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Platform.Storage.Authorization.AppCaller;

/// <summary>
/// A public key version that an org's app tokens may be signed with.
/// </summary>
/// <param name="Org">The org code.</param>
/// <param name="Kid">The Key Vault key version, sent as <c>kid</c> in the token header.</param>
/// <param name="Key">The public key.</param>
public sealed record AppTokenSigningKey(string Org, string Kid, SecurityKey Key);

/// <summary>
/// Reads every usable app token signing key from where they are stored.
/// </summary>
public interface IAppTokenKeySource
{
    /// <summary>
    /// Reads every enabled, unexpired key version of every app token signing key.
    /// Throws if the keys cannot be read in full.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>All usable key versions.</returns>
    Task<IReadOnlyList<AppTokenSigningKey>> GetKeysAsync(CancellationToken cancellationToken);
}
