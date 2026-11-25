namespace Altinn.Platform.Storage.Authorization.Scopes;

/// <summary>
/// How a scope requirement is applied.
/// </summary>
public enum ScopeCheckMode
{
    /// <summary>
    /// The requirement is not checked.
    /// </summary>
    Off,

    /// <summary>
    /// The decision is recorded on the request metric, and missing scopes are logged, but nothing is refused.
    /// </summary>
    Observe,

    /// <summary>
    /// The decision is recorded and logged, and a request with a missing scope is refused with 403.
    /// </summary>
    Enforce,
}
