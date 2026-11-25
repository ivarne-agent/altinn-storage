using System;

namespace Altinn.Platform.Storage.Authorization.Scopes;

/// <summary>
/// Requires a fixed scope on an endpoint, whatever app the request concerns. <see cref="RequireScopeFilter"/>
/// checks it before the action runs.
/// </summary>
/// <param name="scope">The scope the token must have.</param>
/// <param name="access">The scope level the endpoint is recorded with on the request metric.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireScopeAttribute(string scope, ScopeAccess access) : Attribute
{
    /// <summary>
    /// The scope the token must have.
    /// </summary>
    public string Scope { get; } = scope;

    /// <summary>
    /// The scope level the endpoint is recorded with on the request metric.
    /// </summary>
    public ScopeAccess Access { get; } = access;
}
