using Serilog;

namespace Basil.Configuration;

/// <summary>
///     The log file and queue settings of the <c>Logging</c> section; the levels are the standard
///     <c>Logging:LogLevel</c> and apply at once, these apply after a restart.
/// </summary>
public sealed class LoggingOptions
{
	/// <summary>Gets the settings of the log file.</summary>
	public LogFileOptions File { get; set; } = new();

	/// <summary>Gets how many log events wait to be written before the newest are dropped and counted.</summary>
	public int QueueCapacity { get; set; } = 10_000;
}

/// <summary>Where the log files are, when a new one starts and how long they are kept.</summary>
public sealed class LogFileOptions
{
	/// <summary>Gets a value that indicates whether log files are written.</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>Gets the folder of the log files, relative to the executable.</summary>
	public string Location { get; set; } = "Logs";

	/// <summary>Gets when a new log file starts.</summary>
	public RollingOptions Rolling { get; set; } = new();

	/// <summary>Gets which old log files are deleted.</summary>
	public RetentionOptions Retention { get; set; } = new();

	/// <summary>When a new log file starts.</summary>
	public sealed class RollingOptions
	{
		/// <summary>Gets the interval after which a new file starts.</summary>
		public RollingInterval Interval { get; set; } = RollingInterval.Day;

		/// <summary>Gets the size, such as <c>100MB</c>, at which a new file starts before the interval ends; empty for no limit.</summary>
		public string? MaxSize { get; set; } = "100MB";
	}

	/// <summary>Which old log files are deleted; a limit of zero or empty is not applied.</summary>
	public sealed class RetentionOptions
	{
		/// <summary>Gets how long a log file is kept after its last write.</summary>
		public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(14);

		/// <summary>Gets how many log files are kept, the newest first.</summary>
		public int MaxFiles { get; set; } = 30;

		/// <summary>Gets the total size, such as <c>1GB</c>, of the kept log files.</summary>
		public string? MaxSize { get; set; } = "1GB";
	}
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
	/// <summary>The channel of the releases the installer publishes by default.</summary>
	public const string StableChannel = "stable";

	/// <summary>Gets a value that indicates whether the server looks for a newer release when it starts.</summary>
	public bool CheckOnStartup { get; set; } = true;

	/// <summary>Gets the release feed: a GitHub repository URL or a plain feed URL.</summary>
	public string Source { get; set; } = "https://github.com/thnhmai06/osuBasil";

	/// <summary>Gets the channel of releases to look at: <c>stable</c>, or the name of another published channel.</summary>
	public string Channel { get; set; } = StableChannel;
}