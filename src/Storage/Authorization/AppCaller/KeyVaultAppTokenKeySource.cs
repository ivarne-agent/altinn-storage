using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Azure.Security.KeyVault.Keys;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using KeyVaultJsonWebKey = Azure.Security.KeyVault.Keys.JsonWebKey;

namespace Altinn.Platform.Storage.Authorization.AppCaller;

/// <summary>
/// Reads app token signing keys from Azure Key Vault. Every key whose name starts with
/// <see cref="AppCallerSettings.KeyNamePrefix"/> belongs to the org named by the rest of the key name.
/// </summary>
/// <remarks>
/// Key versions are immutable, so converted public keys are reused across polls and only new
/// versions are fetched. Version properties (enabled, expiry) are read on every poll.
/// Not thread safe: it is only called from <see cref="AppTokenKeyPoller"/>.
/// </remarks>
/// <param name="client">The Key Vault key client.</param>
/// <param name="settings">The app caller settings.</param>
/// <param name="timeProvider">The time provider.</param>
/// <param name="logger">The logger.</param>
public sealed class KeyVaultAppTokenKeySource(
    KeyClient client,
    IOptions<AppCallerSettings> settings,
    TimeProvider timeProvider,
    ILogger<KeyVaultAppTokenKeySource> logger
) : IAppTokenKeySource
{
    private Dictionary<string, SecurityKey> _converted = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AppTokenSigningKey>> GetKeysAsync(
        CancellationToken cancellationToken
    )
    {
        string prefix = settings.Value.KeyNamePrefix;
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<AppTokenSigningKey> keys = [];
        Dictionary<string, SecurityKey> converted = new(StringComparer.Ordinal);

        await foreach (
            KeyProperties keyProperties in client.GetPropertiesOfKeysAsync(cancellationToken)
        )
        {
            if (
                keyProperties.Managed
                || !keyProperties.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || keyProperties.Name.Length == prefix.Length
            )
            {
                continue;
            }

            string org = keyProperties.Name[prefix.Length..].ToLowerInvariant();

            await foreach (
                KeyProperties version in client.GetPropertiesOfKeyVersionsAsync(
                    keyProperties.Name,
                    cancellationToken
                )
            )
            {
                if (version.Enabled != true || version.ExpiresOn <= now)
                {
                    continue;
                }

                string cacheKey = $"{keyProperties.Name}/{version.Version}";
                if (!_converted.TryGetValue(cacheKey, out SecurityKey? securityKey))
                {
                    KeyVaultKey key = await client.GetKeyAsync(
                        keyProperties.Name,
                        version.Version,
                        cancellationToken
                    );
                    securityKey = ToSecurityKey(key.Key, version.Version);
                    if (securityKey is null)
                    {
                        logger.LogWarning(
                            "App token signing key {KeyName} version {Version} has unsupported key type {KeyType}",
                            keyProperties.Name,
                            version.Version,
                            key.KeyType
                        );
                        continue;
                    }
                }

                converted[cacheKey] = securityKey;
                keys.Add(new AppTokenSigningKey(org, version.Version, securityKey));
            }
        }

        _converted = converted;
        return keys;
    }

    /// <summary>
    /// Converts the public part of a Key Vault key to a <see cref="SecurityKey"/> with
    /// <see cref="SecurityKey.KeyId"/> set to the key version.
    /// </summary>
    /// <param name="key">The Key Vault key.</param>
    /// <param name="version">The key version.</param>
    /// <returns>The security key, or null if the key type is not supported.</returns>
    internal static SecurityKey? ToSecurityKey(KeyVaultJsonWebKey key, string version)
    {
        if (key.KeyType == KeyType.Ec || key.KeyType == KeyType.EcHsm)
        {
            ECDsa ecdsa = key.ToECDsa(includePrivateParameters: false);
            return new ECDsaSecurityKey(ecdsa) { KeyId = version };
        }

        if (key.KeyType == KeyType.Rsa || key.KeyType == KeyType.RsaHsm)
        {
            RSA rsa = key.ToRSA(includePrivateParameters: false);
            return new RsaSecurityKey(rsa) { KeyId = version };
        }

        return null;
    }
}
