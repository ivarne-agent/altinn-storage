using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Altinn.Platform.Storage.Authorization.Scopes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Altinn.Platform.Storage.OpenApi;

/// <summary>
/// Documents the API scopes an operation requires in the <c>x-altinn-scopes</c> extension, from its
/// <see cref="InstanceScopeAccessAttribute"/> or <see cref="RequireScopeAttribute"/>.
/// </summary>
internal sealed class ScopeRequirements : IOperationFilter
{
    /// <summary>
    /// The name of the extension.
    /// </summary>
    internal const string ExtensionName = "x-altinn-scopes";

    /// <inheritdoc/>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        IList<object> metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        JsonObject? requirement =
            metadata.OfType<InstanceScopeAccessAttribute>().LastOrDefault() is { } instanceScope
                ? Describe(instanceScope.Access)
            : metadata.OfType<RequireScopeAttribute>().LastOrDefault() is { } fixedScope
                ? new JsonObject
                {
                    ["access"] = AccessName(fixedScope.Access),
                    ["required"] = new JsonArray(fixedScope.Scope),
                }
            : null;
        if (requirement is null)
        {
            return;
        }

        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions[ExtensionName] = new JsonNodeExtension(requirement);
    }

    private static JsonObject Describe(ScopeAccess access)
    {
        bool read = access == ScopeAccess.Read;
        string level = AccessName(access);
        return new JsonObject
        {
            ["access"] = level,
            ["users"] = new JsonArray(
                read ? ApiScopeRules.InstancesRead : ApiScopeRules.InstancesWrite,
                ApiScopeRules.PortalEndUser
            ),
            ["serviceOwners"] = new JsonArray(
                read
                    ? ApiScopeRules.ServiceOwnerInstancesRead
                    : ApiScopeRules.ServiceOwnerInstancesWrite
            ),
            ["syncAdapter"] = ApiScopeRules.SyncAdapter,
            ["appDefined"] =
                $"An app's apiScopes.users.{level} replaces {(read ? ApiScopeRules.InstancesRead : ApiScopeRules.InstancesWrite)} for its instances. Its apiScopes.serviceOwners.{level} is accepted besides the built-in service owner scope.",
        };
    }

    private static string AccessName(ScopeAccess access) =>
        access == ScopeAccess.Read ? "read" : "write";
}
