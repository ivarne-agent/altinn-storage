using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Authorization.ABAC.Xacml.JsonProfile;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Authorization;

/// <summary>
/// Interface for the authorization service
/// </summary>
public interface IAuthorization
{
    /// <summary>
    /// Authorize instances, and returns a list of MesseageBoxInstances with information about read and write rights of each instance.
    /// </summary>
    /// <returns>
    /// The authorized instances, or a 503 problem when Altinn Authorization gave no decision.
    /// </returns>
    public Task<(
        List<MessageBoxInstance>? Instances,
        ProblemDetails? Problem
    )> AuthorizeMesseageBoxInstances(List<InstanceInternal> instances, bool keyAccessMode);

    /// <summary>
    /// Authorizes a given action on a storage instance.
    /// </summary>
    /// <returns>
    /// Null when the user is authorized, 403 when the user is not, and 503 when Altinn Authorization
    /// gave no decision.
    /// </returns>
    public Task<ProblemDetails?> AuthorizeInstanceAction(
        InstanceInternal instance,
        string action,
        string? task = null
    );

    /// <summary>
    /// Authorizes the current user to perform <paramref name="action"/> on <paramref name="instance"/>.
    /// Callers with the sync adapter scope are authorized for read, write and delete without asking
    /// the PDP. Otherwise the XACML request includes the instance's process context (current task or
    /// end event), and the decision is cached. A missing instance is refused without asking the PDP.
    /// </summary>
    /// <param name="instance">The instance to authorize against, or null when no instance exist.</param>
    /// <param name="action">The action to authorize, e.g. "read", "write" or "delete".</param>
    /// <returns>
    /// Null when the user is authorized. Otherwise the problem to answer with: 403 when the user is not
    /// authorized or the instance is missing, 503 when Altinn Authorization gave no decision and a retry
    /// may succeed.
    /// </returns>
    public Task<ProblemDetails?> AuthorizeInstanceRequest(
        InstanceInternal? instance,
        string action
    );

    /// <summary>
    /// Loads the instance identified by <paramref name="instanceOwnerPartyId"/> and <paramref name="instanceGuid"/>
    /// and authorizes the current user to perform <paramref name="action"/> on it. In order:
    /// <list type="number">
    /// <item>An instance of another party than <paramref name="instanceOwnerPartyId"/> is treated as missing,
    /// when the party check is enforced.</item>
    /// <item>The API scopes are checked against the scope level the endpoint declares with
    /// <see cref="Scopes.InstanceScopeAccessAttribute"/>, see <see cref="Scopes.IInstanceScopeAuthorizer"/>.
    /// A missing scope is a 403 decided without asking Altinn Authorization.</item>
    /// <item>The action is authorized as <see cref="AuthorizeInstanceRequest"/> does.</item>
    /// </list>
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance.</param>
    /// <param name="action">The action to authorize, e.g. "read", "write" or "delete".</param>
    /// <param name="includeElements">Whether to load the instance's data elements.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Exactly one of <c>Instance</c> and <c>Problem</c> is set. <c>Instance</c> is the loaded instance when
    /// the user is authorized and the instance exists. Otherwise <c>Problem</c> is the response to give: 403
    /// when the user is not authorized or the instance does not exist, and 503 when Altinn Authorization gave
    /// no decision. Only the sync adapter, which may read every instance, gets 404 for a missing instance.
    /// </returns>
    public Task<(InstanceInternal? Instance, ProblemDetails? Problem)> GetAuthorizedInstance(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        string action,
        bool includeElements,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Loads the instance identified by <paramref name="instanceGuid"/> and authorizes the current user as
    /// <see cref="GetAuthorizedInstance(int, Guid, string, bool, CancellationToken)"/> does, for routes without
    /// the instance owner's party. A missing instance gives the sync adapter a 404 without the party id.
    /// </summary>
    public Task<(InstanceInternal? Instance, ProblemDetails? Problem)> GetAuthorizedInstance(
        Guid instanceGuid,
        string action,
        bool includeElements,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Loads the instance, checks the party in the route and the API scopes as
    /// <see cref="GetAuthorizedInstance(int, Guid, string, bool, CancellationToken)"/> does, and then leaves the
    /// decision to <paramref name="authorize"/>. It is for endpoints whose access rules are not a single
    /// action on the instance, such as the process authorizer's task-specific actions, the app owner's own
    /// access, or the lock token.
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance.</param>
    /// <param name="authorize">
    /// Decides whether the caller may use the loaded instance: null when authorized, otherwise the problem to
    /// answer with. Called only for an instance that exists and passed the scope check.
    /// </param>
    /// <param name="includeElements">Whether to load the instance's data elements.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Exactly one of <c>Instance</c> and <c>Problem</c> is set. A missing instance gives 403, or 404 to a
    /// caller with the sync adapter scope, without calling <paramref name="authorize"/>.
    /// </returns>
    public Task<(InstanceInternal? Instance, ProblemDetails? Problem)> GetAuthorizedInstance(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        Func<InstanceInternal, Task<ProblemDetails?>> authorize,
        bool includeElements,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Loads the instance for the process information Altinn Authorization bases its decisions on. The
    /// caller is not authorized: the endpoint that uses this is called by Altinn Authorization itself.
    /// </summary>
    /// <returns>The instance, or null when it is missing or belongs to another party.</returns>
    public Task<InstanceInternal?> GetInstanceForAuthInfo(
        int instanceOwnerPartyId,
        Guid instanceGuid,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Authorizes that the user has one or more of the actions on a storage instance.
    /// </summary>
    /// <returns>
    /// Null when the user is authorized for at least one of the actions. Otherwise 503 when Altinn
    /// Authorization gave no decision, and 403 when it refused.
    /// </returns>
    public Task<ProblemDetails?> AuthorizeAnyOfInstanceActions(
        InstanceInternal instance,
        List<string> actions
    );

    /// <summary>
    /// Authorize storage instances, and returns the instances that the user has the right to read.
    /// </summary>
    /// <returns>
    /// The instances the user may read, or a 503 problem when Altinn Authorization gave no decision.
    /// </returns>
    public Task<(List<InstanceInternal>? Instances, ProblemDetails? Problem)> AuthorizeInstances(
        List<InstanceInternal> instances
    );

    /// <summary>
    /// Verifies that the user has at least one of the supplied scopes.
    /// </summary>
    /// <param name="requiredScope">Required scopes</param>
    /// <returns>true if the current user has any of the scopes provided.</returns>
    public bool UserHasRequiredScope(List<string> requiredScope);

    /// <summary>
    /// Verifies that the user has the supplied scope.
    /// </summary>
    /// <param name="requiredScope">Required scopes</param>
    /// <returns>true if the current user has the scope provided.</returns>
    public bool UserHasRequiredScope(string requiredScope);

    /// <summary>
    /// Sends in a request and get response with result of the request
    /// </summary>
    /// <param name="xacmlJsonRequest">The Xacml Json Request</param>
    /// <returns>The Xacml Json response contains the result of the request</returns>
    public Task<XacmlJsonResponse?> GetDecisionForRequest(XacmlJsonRequestRoot xacmlJsonRequest);
}
