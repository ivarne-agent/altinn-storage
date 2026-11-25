namespace Altinn.Platform.Storage.Authorization.Scopes;

/// <summary>
/// The outcome of a scope check, as recorded in the <c>scope_check</c> tag.
/// </summary>
public enum ScopeCheckOutcome
{
    /// <summary>
    /// A user or system user has the required scope, or <c>altinn:portal/enduser</c>.
    /// </summary>
    Ok,

    /// <summary>
    /// A service owner has the built-in service owner scope.
    /// </summary>
    OkServiceOwnerBuiltIn,

    /// <summary>
    /// A service owner has the scope the app defines for service owners.
    /// </summary>
    OkServiceOwnerApp,

    /// <summary>
    /// The caller has the sync adapter scope, which is accepted on every instance endpoint.
    /// </summary>
    SyncAdapter,

    /// <summary>
    /// The caller lacks the built-in scope the endpoint requires.
    /// </summary>
    MissingBuiltIn,

    /// <summary>
    /// The caller lacks the scope the app defines.
    /// </summary>
    MissingApp,

    /// <summary>
    /// The caller lacks the sync adapter scope that a messagebox endpoint requires.
    /// </summary>
    MissingSyncAdapter,
}
