using System;

namespace Altinn.Platform.Storage.Authorization.Scopes;

/// <summary>
/// Marks an endpoint in an instance controller that has no API scope requirement, with the reason.
/// </summary>
/// <param name="reason">Why the endpoint needs no scope check.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ExcludeFromScopeCheckAttribute(string reason) : Attribute
{
    /// <summary>
    /// Why the endpoint needs no scope check.
    /// </summary>
    public string Reason { get; } = reason;
}
