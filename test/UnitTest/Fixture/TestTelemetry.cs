using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using OpenTelemetry.Metrics;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Fixture;

internal sealed class TestTelemetry
{
    /// <summary>
    /// The request header and metric tag that identify the requests of a test client, see
    /// <see cref="IdentifyRequests"/> and <see cref="RequestIdStartupFilter"/>.
    /// </summary>
    public const string RequestIdName = "X-Test-Request-Id";

    private readonly MeterProvider _meterProvider;

    public ConcurrentList<MetricSnapshot> Metrics { get; }

    public TestTelemetry(MeterProvider meterProvider, ConcurrentList<MetricSnapshot> metrics)
    {
        _meterProvider = meterProvider;
        Metrics = metrics;
    }

    /// <summary>
    /// Gives the requests of <paramref name="client"/> an id of their own, so
    /// <see cref="AssertScopeCheckAsync"/> can find their metric points among those of other hosts in the
    /// process.
    /// </summary>
    public static void IdentifyRequests(HttpClient client) =>
        client.DefaultRequestHeaders.Add(RequestIdName, Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Asserts that the request that gave <paramref name="response"/>, from a client passed to
    /// <see cref="IdentifyRequests"/>, was recorded on the request duration
    /// metric with the scope check outcome <paramref name="expectedOutcome"/> and the scope level
    /// <paramref name="expectedAccess"/>.
    /// </summary>
    public async Task AssertScopeCheckAsync(
        HttpResponseMessage response,
        string expectedOutcome,
        string expectedAccess
    )
    {
        const int maxAttempts = 10;
        const int delayMs = 10;

        IEnumerable<string>? values = null;
        Assert.True(
            response.RequestMessage?.Headers.TryGetValues(RequestIdName, out values) == true,
            $"The request has no {RequestIdName} header."
        );
        string requestId = values!.Single();

        string[] outcomes = [];
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Assert.True(_meterProvider.ForceFlush());
            outcomes = GetScopeChecks(requestId);
            if (outcomes.Length > 0)
            {
                break;
            }

            await Task.Delay(delayMs);
        }

        Assert.Equal([$"{expectedOutcome}/{expectedAccess}"], outcomes);
    }

    private string[] GetScopeChecks(string requestId)
    {
        MetricSnapshot? metric = Metrics.LastOrDefault(m =>
            m.Name == AspNetCoreMetricsEnricher.MetricName
        );
        if (metric == null)
        {
            return [];
        }

        Assert.Equal(MetricType.Histogram, metric.MetricType);
        List<string> outcomes = [];
        foreach (MetricPoint point in metric.MetricPoints)
        {
            string? id = null;
            string? outcome = null;
            string? access = null;
            foreach (KeyValuePair<string, object?> tag in point.Tags)
            {
                switch (tag.Key)
                {
                    case RequestIdName:
                        id = tag.Value as string;
                        break;
                    case "scope_check":
                        outcome = tag.Value as string;
                        break;
                    case "scope_access":
                        access = tag.Value as string;
                        break;
                }
            }

            if (id == requestId)
            {
                outcomes.Add(outcome is null ? "none" : $"{outcome}/{access}");
            }
        }

        return [.. outcomes];
    }

    /// <summary>
    /// Copies the <see cref="RequestIdName"/> header of a request to a tag on the request duration metric.
    /// </summary>
    internal sealed class RequestIdStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(
                    (context, nextMiddleware) =>
                    {
                        string? requestId = context.Request.Headers[RequestIdName];
                        if (!string.IsNullOrEmpty(requestId))
                        {
                            context
                                .Features.Get<IHttpMetricsTagsFeature>()
                                ?.Tags.Add(new(RequestIdName, requestId));
                        }

                        return nextMiddleware(context);
                    }
                );
                next(app);
            };
    }
}
