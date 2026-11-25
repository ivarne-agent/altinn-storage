namespace Altinn.Platform.Storage.Authorization.Scopes;

/// <summary>
/// The API scope level an endpoint requires. It is a property of the endpoint, independent of the
/// XACML action the endpoint authorizes with.
/// </summary>
public enum ScopeAccess
{
    /// <summary>
    /// The client only reads the instance.
    /// </summary>
    Read,

    /// <summary>
    /// The client changes the instance.
    /// </summary>
    Write,
}
