using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Authorization.AppCaller;

/// <summary>
/// Reloads the app token signing keys on a fixed interval and swaps them into
/// <see cref="AppTokenKeySet"/>. A failed poll keeps the previous set.
/// </summary>
/// <param name="source">The key source.</param>
/// <param name="keySet">The key set to update.</param>
/// <param name="settings">The app caller settings.</param>
/// <param name="timeProvider">The time provider.</param>
/// <param name="logger">The logger.</param>
public sealed class AppTokenKeyPoller(
    IAppTokenKeySource source,
    AppTokenKeySet keySet,
    IOptions<AppCallerSettings> settings,
    TimeProvider timeProvider,
    ILogger<AppTokenKeyPoller> logger
) : BackgroundService
{
    /// <summary>
    /// Upper bound on the wait before retrying a failed poll, so a pod that starts while Key Vault
    /// is unavailable does not run without keys for a full interval.
    /// </summary>
    private static readonly TimeSpan _maxRetryDelay = TimeSpan.FromSeconds(30);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, settings.Value.PollIntervalSeconds));
        TimeSpan retryDelay = interval < _maxRetryDelay ? interval : _maxRetryDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            bool succeeded = await PollOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(succeeded ? interval : retryDelay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Loads the keys once and swaps them in. Never throws.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the key set was replaced.</returns>
    internal async Task<bool> PollOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<AppTokenSigningKey> keys = await source.GetKeysAsync(cancellationToken);
            int previousCount = keySet.Count;
            keySet.Replace(keys);

            if (keys.Count == 0)
            {
                logger.LogWarning("Loaded no app token signing keys");
            }
            else if (keys.Count != previousCount)
            {
                logger.LogInformation("Loaded {KeyCount} app token signing keys", keys.Count);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            AppCallerMetrics.KeyPollFailures.Add(1);
            logger.LogError(
                exception,
                "Failed to load app token signing keys, keeping the key set loaded {KeySetAge} ago",
                keySet.KeySetAge
            );
            return false;
        }
    }
}
