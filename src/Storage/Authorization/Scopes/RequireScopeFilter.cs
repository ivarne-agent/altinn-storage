using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Authorization.Scopes;

/// <summary>
/// Checks the fixed scope of endpoints with <see cref="RequireScopeAttribute"/>. The attribute is used for
/// the sync adapter scope on the messagebox endpoints, so the decision is recorded as <c>sync_adapter</c> or
/// <c>missing_syncadapter</c>, governed by <see cref="GeneralSettings.MessageboxSyncAdapterScopeCheck"/>.
/// </summary>
/// <param name="settings">The general settings.</param>
/// <param name="logger">The logger.</param>
internal sealed class RequireScopeFilter(
    IOptions<GeneralSettings> settings,
    ILogger<RequireScopeFilter> logger
) : IAsyncActionFilter
{
    /// <inheritdoc/>
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next
    )
    {
        RequireScopeAttribute? attribute = context
            .ActionDescriptor.EndpointMetadata.OfType<RequireScopeAttribute>()
            .LastOrDefault();
        ScopeCheckMode mode = settings.Value.MessageboxSyncAdapterScopeCheck;
        if (attribute is null || mode == ScopeCheckMode.Off)
        {
            await next();
            return;
        }

        bool hasScope = ApiScopeRules.GetScopes(context.HttpContext.User).Contains(attribute.Scope);
        ScopeDecision decision = new(
            hasScope ? ScopeCheckOutcome.SyncAdapter : ScopeCheckOutcome.MissingSyncAdapter,
            attribute.Access,
            mode
        );

        ScopeCheckTelemetry.Record(context.HttpContext, decision);
        if (decision.IsMissing)
        {
            ScopeCheckTelemetry.LogMissingScope(logger, context.HttpContext, decision, null);
        }

        if (decision.Refuses)
        {
            context.Result = AuthorizationProblems.InsufficientScope(decision.Access).ToResult();
            return;
        }

        await next();
    }
}
