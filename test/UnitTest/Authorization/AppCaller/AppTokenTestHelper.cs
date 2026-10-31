using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Altinn.Platform.Storage.Authorization.AppCaller;
using Altinn.Platform.Storage.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace Altinn.Platform.Storage.UnitTest.Authorization.AppCaller;

/// <summary>
/// Creates signing keys and app tokens for tests.
/// </summary>
internal static class AppTokenTestHelper
{
    public const string Issuer = "altinn-studio-operator";
    public const string Audience = "altinn-storage";

    public static ECDsaSecurityKey CreateEcKey(string kid) =>
        new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = kid };

    public static RsaSecurityKey CreateRsaKey(string kid) => new(RSA.Create(2048)) { KeyId = kid };

    /// <summary>
    /// The public part of a key, as Storage holds it.
    /// </summary>
    public static SecurityKey PublicKey(SecurityKey key) =>
        key switch
        {
            ECDsaSecurityKey ec => new ECDsaSecurityKey(
                ECDsa.Create(ec.ECDsa.ExportParameters(false))
            )
            {
                KeyId = ec.KeyId,
            },
            RsaSecurityKey rsa => new RsaSecurityKey(rsa.Rsa.ExportParameters(false))
            {
                KeyId = rsa.KeyId,
            },
            _ => throw new ArgumentException("Unsupported key", nameof(key)),
        };

    public static string CreateToken(
        SecurityKey signingKey,
        string org = "ttd",
        string? app = null,
        string issuer = Issuer,
        string audience = Audience,
        DateTime? notBefore = null,
        DateTime? expires = null,
        string? algorithm = null,
        bool includeExpiry = true
    )
    {
        DateTime now = DateTime.UtcNow;
        Dictionary<string, object> claims = new()
        {
            [AppTokenValidator.OrgClaim] = org,
            [AppTokenValidator.AppClaim] = app ?? $"{org}/test-app",
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
        };

        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = notBefore ?? now.AddMinutes(-1),
            Expires = includeExpiry ? expires ?? now.AddHours(2) : null,
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                signingKey,
                algorithm
                    ?? (
                        signingKey is RsaSecurityKey
                            ? SecurityAlgorithms.RsaSha256
                            : SecurityAlgorithms.EcdsaSha256
                    )
            ),
        };

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(
            descriptor
        );
    }

    public static string CreateUnsignedToken(string kid, string org = "ttd")
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string header = $$"""{"alg":"none","typ":"JWT","kid":"{{kid}}"}""";
        string payload =
            $$"""{"iss":"{{Issuer}}","aud":"{{Audience}}","urn:altinn:org":"{{org}}","urn:altinn:app":"{{org}}/test-app","nbf":{{now
                - 60}},"exp":{{now + 3600}}}""";
        return $"{Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(header))}.{Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload))}.";
    }

    public static IOptionsMonitor<AppCallerSettings> Monitor(AppCallerSettings settings) =>
        Mock.Of<IOptionsMonitor<AppCallerSettings>>(monitor => monitor.CurrentValue == settings);
}
