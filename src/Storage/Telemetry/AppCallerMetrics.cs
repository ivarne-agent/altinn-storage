using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace Altinn.Platform.Storage.Telemetry;

/// <summary>
/// Metrics for app caller token validation.
/// </summary>
internal static class AppCallerMetrics
{
    /// <summary>
    /// Counts app token checks on app-only endpoints, tagged with outcome, reason, mode and org.
    /// Org is only set from trusted sources (a verified signing key or a stored instance) to
    /// keep the tag bounded.
    /// </summary>
    public static readonly Counter<long> Validations = Metrics.Meter.CreateCounter<long>(
        "storage.app_caller.validations",
        description: "App caller token checks on app-only endpoints"
    );

    /// <summary>
    /// Counts failed polls of the app token signing keys.
    /// </summary>
    public static readonly Counter<long> KeyPollFailures = Metrics.Meter.CreateCounter<long>(
        "storage.app_caller.key_poll_failures",
        description: "Failed polls of the app token signing keys"
    );

    /// <summary>
    /// Records one app token check.
    /// </summary>
    public static void RecordValidation(string outcome, string reason, string mode, string? org) =>
        Validations.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("reason", reason),
            new KeyValuePair<string, object?>("mode", mode),
            new KeyValuePair<string, object?>("org", org ?? "unknown")
        );
}
