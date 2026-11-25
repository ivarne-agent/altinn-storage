using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Altinn.AccessManagement.Core.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.Telemetry;

/// <summary>
/// Enriches the 'http.server.request.duration' metric with client.id and client.consumer.id tags. The scope
/// check adds its own tags where it decides, see <see cref="Authorization.Scopes.ScopeCheckTelemetry"/>.
/// </summary>
internal sealed class AspNetCoreMetricsEnricher(ILogger<AspNetCoreMetricsEnricher> logger)
    : IAsyncActionFilter
{
    /// <summary>
    /// Name of the metric that is enriched
    /// </summary>
    public const string MetricName = "http.server.request.duration";

    private readonly ILogger<AspNetCoreMetricsEnricher> _logger = logger;

    /// <summary>
    /// Executes the filter
    /// </summary>
    /// <param name="context">context</param>
    /// <param name="next">next</param>
    /// <returns></returns>
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var feature = context.HttpContext.Features.Get<IHttpMetricsTagsFeature>();
        var user = context.HttpContext.User;
        if (feature is null || user is null)
        {
            return next();
        }

        var clientId = user.FindFirstValue("client_id");
        var consumer = user.FindFirstValue("consumer");
        string? consumerId = null;
        if (!string.IsNullOrWhiteSpace(consumer))
        {
            try
            {
                var orgClaim = JsonSerializer.Deserialize<OrgClaim>(consumer);
                consumerId = orgClaim?.ID;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize consumer claim in token");
            }
        }

        feature.Tags.Add(new KeyValuePair<string, object?>("client.id", clientId));
        feature.Tags.Add(new KeyValuePair<string, object?>("client.consumer.id", consumerId));

        return next();
    }
}

/// <summary>
/// Registers <see cref="AspNetCoreMetricsEnricher"/>.
/// </summary>
internal static class AspNetCoreMetricsEnricherDI
{
    /// <summary>
    /// Add the filter to the DI container
    /// </summary>
    /// <param name="services">The service collection</param>
    public static void AddAspNetCoreMetricsEnricher(this IServiceCollection services)
    {
        services.Configure<MvcOptions>(options =>
        {
            options.Filters.Add<AspNetCoreMetricsEnricher>();
        });
    }
}
