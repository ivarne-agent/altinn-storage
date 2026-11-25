using System;

namespace Altinn.Platform.Storage.Authorization.Scopes;

/// <summary>
/// Declares the API scope level an instance endpoint requires. The scopes that satisfy it depend on the
/// caller and on the app's <c>apiScopes</c>, see <see cref="ApiScopeRules"/>. The check runs where the
/// endpoint gets its instance or application through the authorization service.
/// </summary>
/// <param name="access">The scope level the endpoint requires.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InstanceScopeAccessAttribute(ScopeAccess access) : Attribute
{
    /// <summary>
    /// The scope level the endpoint requires.
    /// </summary>
    public ScopeAccess Access { get; } = access;
}
