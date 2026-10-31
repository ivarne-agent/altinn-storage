using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Platform.Storage.Authorization.AppCaller;

/// <summary>
/// Validates app tokens issued by the Altinn Studio runtime operator. The signing key is chosen
/// from the org claim and the <c>kid</c> header, so a token can only be verified with a key that
/// belongs to the org it claims.
/// </summary>
/// <param name="keyProvider">The signing key provider.</param>
/// <param name="settings">The app caller settings.</param>
public sealed class AppTokenValidator(
    IAppTokenSigningKeyProvider keyProvider,
    IOptionsMonitor<AppCallerSettings> settings
) : IAppCallerTokenValidator
{
    /// <summary>
    /// The claim holding the org code.
    /// </summary>
    public const string OrgClaim = "urn:altinn:org";

    /// <summary>
    /// The claim holding the <c>{org}/{app}</c> app id.
    /// </summary>
    public const string AppClaim = "urn:altinn:app";

    private static readonly string[] _validAlgorithms =
    [
        SecurityAlgorithms.EcdsaSha256,
        SecurityAlgorithms.RsaSha256,
    ];

    private readonly JsonWebTokenHandler _handler = new();

    /// <inheritdoc/>
    public async Task<AppCallerValidationResult> ValidateAsync(
        string? token,
        string expectedAppId,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(AppCallerOutcome.Missing, AppCallerReasons.Missing);
        }

        JsonWebToken jwt;
        try
        {
            jwt = _handler.ReadJsonWebToken(token);
        }
        catch (ArgumentException)
        {
            return new(AppCallerOutcome.Invalid, AppCallerReasons.Malformed);
        }

        string? kid = string.IsNullOrEmpty(jwt.Kid) ? null : jwt.Kid;

        if (Array.IndexOf(_validAlgorithms, jwt.Alg) < 0)
        {
            return new(AppCallerOutcome.Invalid, AppCallerReasons.BadAlgorithm, Kid: kid);
        }

        if (
            !jwt.TryGetPayloadValue(OrgClaim, out string? claimedOrg)
            || string.IsNullOrEmpty(claimedOrg)
        )
        {
            return new(AppCallerOutcome.Invalid, AppCallerReasons.MissingOrg, Kid: kid);
        }

        if (kid is null || !keyProvider.TryGetKey(claimedOrg, kid, out SecurityKey? key))
        {
            return new(AppCallerOutcome.Invalid, AppCallerReasons.UnknownKid, Kid: kid);
        }

        string org = claimedOrg.ToLowerInvariant();
        AppCallerSettings current = settings.CurrentValue;
        TokenValidationParameters parameters = new()
        {
            ValidIssuer = current.Issuer,
            ValidAudience = current.Audience,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            IssuerSigningKey = key,
            TryAllIssuerSigningKeys = false,
            ValidAlgorithms = _validAlgorithms,
            ValidTypes = ["JWT"],
            ClockSkew = TimeSpan.FromSeconds(current.ClockSkewSeconds),
        };

        TokenValidationResult result = await _handler.ValidateTokenAsync(jwt, parameters);
        if (!result.IsValid)
        {
            return new(AppCallerOutcome.Invalid, MapFailure(result.Exception), org, Kid: kid);
        }

        if (!jwt.TryGetPayloadValue(AppClaim, out string? appId) || !IsAppOfOrg(appId, org))
        {
            return new(AppCallerOutcome.Invalid, AppCallerReasons.BadAppClaim, org, Kid: kid);
        }

        if (!string.Equals(appId, expectedAppId, StringComparison.OrdinalIgnoreCase))
        {
            return new(AppCallerOutcome.WrongApp, AppCallerReasons.WrongApp, org, appId, kid);
        }

        return new(AppCallerOutcome.Valid, AppCallerReasons.Ok, org, appId, kid);
    }

    private static bool IsAppOfOrg(string? appId, string org)
    {
        if (string.IsNullOrEmpty(appId))
        {
            return false;
        }

        int slash = appId.IndexOf('/');
        return slash == org.Length
            && slash < appId.Length - 1
            && appId.IndexOf('/', slash + 1) < 0
            && appId.StartsWith(org, StringComparison.OrdinalIgnoreCase);
    }

    private static string MapFailure(Exception? exception) =>
        exception switch
        {
            SecurityTokenExpiredException => AppCallerReasons.Expired,
            SecurityTokenNotYetValidException => AppCallerReasons.NotYetValid,
            SecurityTokenNoExpirationException => AppCallerReasons.BadLifetime,
            SecurityTokenInvalidLifetimeException => AppCallerReasons.BadLifetime,
            SecurityTokenInvalidAudienceException => AppCallerReasons.WrongAudience,
            SecurityTokenInvalidIssuerException => AppCallerReasons.WrongIssuer,
            SecurityTokenInvalidAlgorithmException => AppCallerReasons.BadAlgorithm,
            SecurityTokenInvalidTypeException => AppCallerReasons.BadType,
            SecurityTokenSignatureKeyNotFoundException => AppCallerReasons.BadSignature,
            SecurityTokenInvalidSignatureException => AppCallerReasons.BadSignature,
            SecurityTokenMalformedException => AppCallerReasons.Malformed,
            _ => AppCallerReasons.Invalid,
        };
}
