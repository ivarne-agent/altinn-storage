#nullable disable

using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Telemetry;
using OpenTelemetry.Metrics;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Fixture;

internal sealed class TestTelemetry
{
    private readonly MeterProvider _meterProvider;

    public ConcurrentList<MetricSnapshot> Metrics { get; }

    public TestTelemetry(MeterProvider meterProvider, ConcurrentList<MetricSnapshot> metrics)
    {
        _meterProvider = meterProvider;
        Metrics = metrics;
    }

    /// <summary>
    /// Asserts that a request was recorded on the request duration metric with the scope check outcome
    /// <paramref name="expectedOutcome"/> and the scope level <paramref name="expectedAccess"/>.
    /// </summary>
    public async Task AssertScopeCheckAsync(string expectedOutcome, string expectedAccess)
    {
        const int maxAttempts = 10;
        const int delayMs = 10;

        string[] outcomes = [];
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Assert.True(_meterProvider.ForceFlush());
            outcomes = GetScopeChecks();
            if (outcomes.Contains($"{expectedOutcome}/{expectedAccess}"))
            {
                return;
            }

            await Task.Delay(delayMs);
        }

        Assert.Contains($"{expectedOutcome}/{expectedAccess}", outcomes);
    }

    private string[] GetScopeChecks()
    {
        MetricSnapshot metric = Metrics.LastOrDefault(m =>
            m.Name == AspNetCoreMetricsEnricher.MetricName
        );
        if (metric == null)
        {
            return [];
        }

        Assert.Equal(MetricType.Histogram, metric.MetricType);
        return metric
            .MetricPoints.Select(point =>
            {
                string outcome = null;
                string access = null;
                foreach (var tag in point.Tags)
                {
                    if (tag.Key == "scope_check")
                    {
                        outcome = tag.Value as string;
                    }
                    else if (tag.Key == "scope_access")
                    {
                        access = tag.Value as string;
                    }
                }

                return outcome is null ? null : $"{outcome}/{access}";
            })
            .Where(outcome => outcome is not null)
            .ToArray();
    }
}
