using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using Altinn.AccessManagement.Core.Models;
using Altinn.Platform.Storage.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Authorization.Scopes;

/// <summary>
/// Records scope decisions on the <c>http.server.request.duration</c> metric and in the log.
/// </summary>
internal static partial class ScopeCheckTelemetry
{
    /// <summary>
    /// The tag with the outcome of the scope check.
    /// </summary>
    internal const string OutcomeTag = "scope_check";

    /// <summary>
    /// The tag with the scope level of the endpoint.
    /// </summary>
    internal const string AccessTag = "scope_access";

    private static readonly object _recordedKey = new();

    /// <summary>
    /// Adds the decision to the request metric, unless the decision's mode is off or the request already
    /// has a decision. An endpoint that authorizes several times records its first decision only.
    /// </summary>
    internal static void Record(HttpContext httpContext, ScopeDecision decision)
    {
        if (!decision.IsObserved || !httpContext.Items.TryAdd(_recordedKey, decision))
        {
            return;
        }

        IHttpMetricsTagsFeature? feature = httpContext.Features.Get<IHttpMetricsTagsFeature>();
        if (feature is null)
        {
            return;
        }

        feature.Tags.Add(new KeyValuePair<string, object?>(OutcomeTag, decision.OutcomeTag));
        feature.Tags.Add(new KeyValuePair<string, object?>(AccessTag, decision.AccessTag));
    }

    /// <summary>
    /// Logs a refusal, or a request that would have been refused, with what identifies the client. The
    /// token itself is never logged.
    /// </summary>
    internal static void LogMissingScope(
        ILogger logger,
        HttpContext httpContext,
        ScopeDecision decision,
        string? appId
    )
    {
        ClaimsPrincipal user = httpContext.User;
        LogMissingScope(
            logger,
            decision.OutcomeTag,
            decision.Mode,
            decision.AccessTag,
            httpContext.GetEndpoint()?.DisplayName,
            appId,
            user.FindFirstValue("client_id"),
            GetConsumerId(user),
            GetCallerType(user),
            string.Join(' ', ApiScopeRules.GetScopes(user).Order(StringComparer.Ordinal))
        );
    }

    /// <summary>
    /// The kind of caller, for logs.
    /// </summary>
    internal static string GetCallerType(ClaimsPrincipal user)
    {
        if (user.GetOrg() is not null)
        {
            return "service_owner";
        }

        if (user.GetSystemUser() is not null)
        {
            return "system_user";
        }

        return user.GetUserId() is not null ? "user" : "unknown";
    }

    /// <summary>
    /// The organization number of the consumer claim, or null.
    /// </summary>
    internal static string? GetConsumerId(ClaimsPrincipal user)
    {
        string? consumer = user.FindFirstValue("consumer");
        if (string.IsNullOrWhiteSpace(consumer))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OrgClaim>(consumer)?.ID;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "API scope check {Outcome} in mode {Mode} for {Access} on {Endpoint}. App: {AppId}, client: {ClientId}, consumer: {ConsumerId}, caller: {CallerType}, scopes: {Scopes}"
    )]
    private static partial void LogMissingScope(
        ILogger logger,
        string outcome,
        ScopeCheckMode mode,
        string access,
        string? endpoint,
        string? appId,
        string? clientId,
        string? consumerId,
        string callerType,
        string scopes
    );
}
