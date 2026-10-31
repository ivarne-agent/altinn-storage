using System;

namespace Altinn.Platform.Storage.Configuration;

/// <summary>
/// How Storage treats the <c>Altinn-App-Token</c> on endpoints that only Altinn apps call.
/// </summary>
public enum AppCallerMode
{
    /// <summary>
    /// The token is ignored.
    /// </summary>
    Off,

    /// <summary>
    /// The token is validated, logged and counted, but the request is let through.
    /// </summary>
    Observe,

    /// <summary>
    /// Requests without a valid token for the target app are rejected.
    /// </summary>
    Enforce,
}

/// <summary>
/// Settings for authenticating Altinn apps through the <c>Altinn-App-Token</c> header.
/// </summary>
public class AppCallerSettings
{
    /// <summary>
    /// Gets or sets how the token is treated.
    /// </summary>
    public AppCallerMode Mode { get; set; } = AppCallerMode.Off;

    /// <summary>
    /// Gets or sets the URI of the Key Vault that holds the app token signing keys.
    /// When null, no keys are loaded and every token fails with <c>unknown_kid</c>.
    /// </summary>
    public Uri? KeyVaultUri { get; set; }

    /// <summary>
    /// Gets or sets the Key Vault key name prefix. The rest of the key name is the org code.
    /// </summary>
    public string KeyNamePrefix { get; set; } = "app-token-signer-";

    /// <summary>
    /// Gets or sets how often the signing keys are reloaded from Key Vault.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets the clock skew allowed when validating token lifetime.
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the required token audience.
    /// </summary>
    public string Audience { get; set; } = "altinn-storage";

    /// <summary>
    /// Gets or sets the required token issuer.
    /// </summary>
    public string Issuer { get; set; } = "altinn-studio-operator";
}
