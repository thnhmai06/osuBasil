using Serilog;

namespace Basil.Configuration;

public sealed class LoggingOptions
{
	public LogFileOptions File { get; set; } = new();

	public int QueueCapacity { get; set; } = 10_000;
}

public sealed class LogFileOptions
{
	public bool Enabled { get; set; } = true;

	public string Location { get; set; } = "Logs";

	public RollingOptions Rolling { get; set; } = new();

	public RetentionOptions Retention { get; set; } = new();

	public sealed class RollingOptions
	{
		public RollingInterval Interval { get; set; } = RollingInterval.Day;

		public string? MaxSize { get; set; } = "100MB";
	}

	public sealed class RetentionOptions
	{
		public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(14);

		public int MaxFiles { get; set; } = 30;

		public string? MaxSize { get; set; } = "1GB";
	}
}

public sealed class TelemetryOptions
{
	public string? OtlpEndpoint { get; set; }

	public bool Metrics { get; set; }

	public bool Traces { get; set; }

	public bool Logs { get; set; }
}

public sealed class UpdateOptions
{
	public const string StableChannel = "stable";

	public bool CheckOnStartup { get; set; } = true;

	public string Source { get; set; } = "https://github.com/thnhmai06/osuBasil";

	public string Channel { get; set; } = StableChannel;
}