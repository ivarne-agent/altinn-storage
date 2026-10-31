using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Authorization.AppCaller;

/// <summary>
/// Reports the age of the app token signing key set. A missing or stale set is reported as
/// degraded rather than unhealthy, so a Key Vault outage does not take Storage out of rotation.
/// </summary>
/// <param name="keySet">The key set.</param>
/// <param name="settings">The app caller settings.</param>
public sealed class AppTokenKeySetHealthCheck(
    AppTokenKeySet keySet,
    IOptions<AppCallerSettings> settings
) : IHealthCheck
{
    /// <summary>
    /// The number of poll intervals after which the key set is considered stale.
    /// </summary>
    private const int _staleAfterIntervals = 3;

    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        TimeSpan? age = keySet.KeySetAge;
        if (age is null)
        {
            return Task.FromResult(
                HealthCheckResult.Degraded("App token signing keys have not been loaded")
            );
        }

        Dictionary<string, object> data = new()
        {
            ["ageSeconds"] = (long)age.Value.TotalSeconds,
            ["keyCount"] = keySet.Count,
        };

        TimeSpan staleAfter = TimeSpan.FromSeconds(
            settings.Value.PollIntervalSeconds * _staleAfterIntervals
        );

        return Task.FromResult(
            age.Value > staleAfter
                ? HealthCheckResult.Degraded("App token signing keys are stale", data: data)
                : HealthCheckResult.Healthy("App token signing keys are loaded", data)
        );
    }
}
