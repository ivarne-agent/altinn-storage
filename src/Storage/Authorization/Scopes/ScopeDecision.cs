namespace Altinn.Platform.Storage.Authorization.Scopes;

/// <summary>
/// A scope decision and the mode that governs it.
/// </summary>
/// <param name="Outcome">What the check found.</param>
/// <param name="Access">The scope level the endpoint requires.</param>
/// <param name="Mode">The setting that governs this kind of requirement.</param>
public readonly record struct ScopeDecision(
    ScopeCheckOutcome Outcome,
    ScopeAccess Access,
    ScopeCheckMode Mode
)
{
    /// <summary>
    /// Whether the token lacks the required scope.
    /// </summary>
    public bool IsMissing =>
        Outcome
            is ScopeCheckOutcome.MissingBuiltIn
                or ScopeCheckOutcome.MissingApp
                or ScopeCheckOutcome.MissingSyncAdapter;

    /// <summary>
    /// Whether the request is refused.
    /// </summary>
    public bool Refuses => IsMissing && Mode == ScopeCheckMode.Enforce;

    /// <summary>
    /// Whether the decision is recorded and, when the scope is missing, logged.
    /// </summary>
    public bool IsObserved => Mode != ScopeCheckMode.Off;

    /// <summary>
    /// The value of the <c>scope_check</c> tag.
    /// </summary>
    public string OutcomeTag =>
        Outcome switch
        {
            ScopeCheckOutcome.Ok => "ok",
            ScopeCheckOutcome.OkServiceOwnerBuiltIn => "ok_serviceowner_builtin",
            ScopeCheckOutcome.OkServiceOwnerApp => "ok_serviceowner_app",
            ScopeCheckOutcome.SyncAdapter => "sync_adapter",
            ScopeCheckOutcome.MissingBuiltIn => "missing_builtin",
            ScopeCheckOutcome.MissingApp => "missing_app",
            ScopeCheckOutcome.MissingSyncAdapter => "missing_syncadapter",
            _ => "unknown",
        };

    /// <summary>
    /// The value of the <c>scope_access</c> tag.
    /// </summary>
    public string AccessTag => Access == ScopeAccess.Read ? "read" : "write";
}
