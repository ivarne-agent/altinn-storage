namespace Altinn.Platform.Storage.Authorization.AppCaller;

/// <summary>
/// The outcome of validating an app token.
/// </summary>
public enum AppCallerOutcome
{
    /// <summary>
    /// The token is valid and was issued to the target app.
    /// </summary>
    Valid,

    /// <summary>
    /// No token was sent.
    /// </summary>
    Missing,

    /// <summary>
    /// The token could not be validated.
    /// </summary>
    Invalid,

    /// <summary>
    /// The token is valid but was issued to another app.
    /// </summary>
    WrongApp,
}

/// <summary>
/// Reason codes for <see cref="AppCallerValidationResult.Reason"/>. They are used as metric tags,
/// so the set must stay small and fixed.
/// </summary>
public static class AppCallerReasons
{
    /// <summary>The token is valid.</summary>
    public const string Ok = "ok";

    /// <summary>No token was sent.</summary>
    public const string Missing = "missing";

    /// <summary>The token is not a well-formed JWS.</summary>
    public const string Malformed = "malformed";

    /// <summary>The token has no <c>urn:altinn:org</c> claim.</summary>
    public const string MissingOrg = "missing_org";

    /// <summary>No signing key is known for the org claim and <c>kid</c>.</summary>
    public const string UnknownKid = "unknown_kid";

    /// <summary>The signature does not verify.</summary>
    public const string BadSignature = "bad_signature";

    /// <summary>The <c>alg</c> header is not an accepted algorithm.</summary>
    public const string BadAlgorithm = "bad_algorithm";

    /// <summary>The <c>typ</c> header is not <c>JWT</c>.</summary>
    public const string BadType = "bad_type";

    /// <summary>The token has expired.</summary>
    public const string Expired = "expired";

    /// <summary>The token is not valid yet.</summary>
    public const string NotYetValid = "not_yet_valid";

    /// <summary>The token has no expiry or other lifetime claims are unusable.</summary>
    public const string BadLifetime = "bad_lifetime";

    /// <summary>The audience does not match.</summary>
    public const string WrongAudience = "wrong_audience";

    /// <summary>The issuer does not match.</summary>
    public const string WrongIssuer = "wrong_issuer";

    /// <summary>The <c>urn:altinn:app</c> claim is missing or does not belong to the org claim.</summary>
    public const string BadAppClaim = "bad_app_claim";

    /// <summary>The token was issued to another app.</summary>
    public const string WrongApp = "wrong_app";

    /// <summary>The token failed validation for another reason.</summary>
    public const string Invalid = "invalid";
}

/// <summary>
/// The result of validating an app token.
/// </summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Reason">One of <see cref="AppCallerReasons"/>.</param>
/// <param name="Org">
/// The org the signing key belongs to. Null unless a known key was found for the token,
/// so it only ever holds orgs that have a key in Key Vault.
/// </param>
/// <param name="AppId">The <c>urn:altinn:app</c> claim, set when the signature verified.</param>
/// <param name="Kid">The <c>kid</c> header, as sent.</param>
public sealed record AppCallerValidationResult(
    AppCallerOutcome Outcome,
    string Reason,
    string? Org = null,
    string? AppId = null,
    string? Kid = null
);
