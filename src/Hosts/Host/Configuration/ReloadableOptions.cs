namespace Basil.Host.Configuration;

/// <summary>Logging settings; a change applies at once.</summary>
public sealed class LoggingOptions
{
	/// <summary>Gets the lowest level written for a source that has no override.</summary>
	public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

	/// <summary>Gets the lowest level written for the sources that start with a name, which beats <see cref="MinimumLevel" />.</summary>
	public Dictionary<string, LogLevel> Overrides { get; set; } = new()
	{
		["Microsoft"] = LogLevel.Warning,
		["Microsoft.Hosting.Lifetime"] = LogLevel.Information,
		["System"] = LogLevel.Warning
	};

	/// <summary>Gets how long log files are kept.</summary>
	public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(14);

	/// <summary>Gets the folder of the log files, relative to the executable.</summary>
	public string Directory { get; set; } = "Logs";

	/// <summary>Gets how many log events wait to be written before the newest are dropped and counted.</summary>
	public int QueueCapacity { get; set; } = 10_000;
}

/// <summary>
///     OpenTelemetry settings; nothing is exported unless an endpoint is set and a signal is switched on. A change
///     applies after a restart.
/// </summary>
public sealed class TelemetryOptions
{
	/// <summary>Gets the OTLP endpoint.</summary>
	public string? OtlpEndpoint { get; set; }

	/// <summary>Gets a value that indicates whether metrics are exported.</summary>
	public bool Metrics { get; set; }

	/// <summary>Gets a value that indicates whether traces are exported.</summary>
	public bool Traces { get; set; }

	/// <summary>Gets a value that indicates whether logs are exported.</summary>
	public bool Logs { get; set; }
}

/// <summary>Update check settings; a change applies at once.</summary>
public sealed class UpdateOptions
{
	/// <summary>Gets a value that indicates whether the server looks for a newer release when it starts.</summary>
	public bool CheckOnStartup { get; set; } = true;

	/// <summary>Gets the release feed: a GitHub repository URL or a plain feed URL.</summary>
	public string Source { get; set; } = "https://github.com/thnhmai06/osuBasil";
}