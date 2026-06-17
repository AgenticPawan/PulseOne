using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PulseOne.SharedKernel.Logging;

/// <summary>
/// Shared OpenTelemetry source names and ambient instruments. Every layer exports
/// traces/metrics/logs to Azure Monitor (global-context.md). Register these source
/// names with the OpenTelemetry tracer/meter providers in each host.
/// </summary>
public static class Telemetry
{
    public const string ServiceName = "PulseOne";

    /// <summary>ActivitySource for manual spans across PulseOne services.</summary>
    public static readonly ActivitySource ActivitySource = new(ServiceName);

    /// <summary>Meter for PulseOne custom metrics (e.g. tenant-resolution failures).</summary>
    public static readonly Meter Meter = new(ServiceName);
}
