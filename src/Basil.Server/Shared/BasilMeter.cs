using System.Diagnostics.Metrics;

namespace Basil.Server.Shared;

/// <summary>The single meter every Basil instrument is published under.</summary>
/// <remarks>
///     Deliberately not a full OpenTelemetry setup: no exporter is wired anywhere. A host that
///     wants the data attaches a listener — <c>dotnet-counters monitor --process-id &lt;pid&gt;
///     Basil</c>, or the diagnostic slice's own listener.
/// </remarks>
public static class BasilMeter
{
	public const string Name = "Basil";
	public static readonly Meter Instance = new(Name);
}
