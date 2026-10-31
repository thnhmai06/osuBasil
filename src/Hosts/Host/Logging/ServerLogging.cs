using System.Text;
using Basil.Host.Cli;
using Basil.Host.Configuration;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.Async;
using Serilog.Sinks.SystemConsole.Themes;

namespace Basil.Host.Logging;

/// <summary>Counts the log events that were dropped because the queue of the writer was full.</summary>
public sealed class LogDrops : IAsyncLogEventSinkMonitor
{
	private volatile IAsyncLogEventSinkInspector? _inspector;
	private long _stopped;

	/// <summary>Gets how many events wait in the queue to be written.</summary>
	public int Pending => _inspector?.Count ?? 0;

	/// <summary>Gets how many events were dropped since the server started.</summary>
	public long Dropped => _inspector?.DroppedMessagesCount ?? Interlocked.Read(ref _stopped);

	/// <inheritdoc />
	public void StartMonitoring(IAsyncLogEventSinkInspector inspector)
	{
		_inspector = inspector;
	}

	/// <inheritdoc />
	public void StopMonitoring(IAsyncLogEventSinkInspector inspector)
	{
		Interlocked.Exchange(ref _stopped, inspector.DroppedMessagesCount);
		_inspector = null;
	}
}

/// <summary>
///     Sets up the server's logging: lifecycle events only, written to a rolling file and, without a dashboard, to
///     stdout.
/// </summary>
public static class ServerLogging
{
	/// <summary>The size at which a log file rolls over before the day ends.</summary>
	public const long FileSizeLimit = 256L * 1024 * 1024;

	private const string Template =
		"{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";

	/// <summary>Adds the logging to the builder.</summary>
	/// <param name="builder">The builder.</param>
	/// <param name="mode">How the server shows itself in its terminal.</param>
	/// <returns>The counter of dropped events.</returns>
	public static LogDrops Add(WebApplicationBuilder builder, CliMode mode)
	{
		var options = builder.Configuration.GetSection("Basil:Logging").Get<LoggingOptions>() ?? new LoggingOptions();
		var telemetry = builder.Configuration.GetSection("Basil:Telemetry").Get<TelemetryOptions>() ??
		                new TelemetryOptions();
		var drops = new LogDrops();
		var directory = ConfigurationSetup.Resolve(options.Directory);
		var logger = Create(configuration =>
		{
			// The file opens at the first event, so a command run from the shell leaves no empty log behind.
			configuration.Sink(new LazySink(() => new LoggerConfiguration().WriteTo.File(
				Path.Combine(directory, "basil-.log"),
				outputTemplate: Template, rollingInterval: RollingInterval.Day, fileSizeLimitBytes: FileSizeLimit,
				rollOnFileSizeLimit: true, retainedFileCountLimit: null, encoding: Encoding.UTF8).CreateLogger()));
			if (mode == CliMode.NoGui) configuration.Console(outputTemplate: Template, theme: ConsoleTheme.None);
			if (telemetry.Logs && telemetry.OtlpEndpoint is { Length: > 0 } endpoint)
				configuration.OpenTelemetry(sink => sink.Endpoint = endpoint);
		}, options.QueueCapacity, drops);

		builder.Logging.ClearProviders();
		builder.Logging.SetMinimumLevel(LogLevel.Trace);
		builder.Logging.AddSerilog(logger, true);
		builder.Services.AddSingleton(drops);
		builder.Services.AddSingleton<LogLevelPolicy>();
		builder.Services.AddOptions<LoggingOptions>().Bind(builder.Configuration.GetSection("Basil:Logging"));
		builder.Services.AddOptions<LoggerFilterOptions>().Configure<LogLevelPolicy>((filter, policy) =>
			filter.Rules.Add(new LoggerFilterRule(null, null, null,
				(_, category, level) => policy.Enabled(category, level))));
		builder.Services.AddHostedService<LogRetentionJob>();
		return drops;
	}

	/// <summary>
	///     Builds a logger whose sinks are written by one background thread behind a queue: when the queue is full the
	///     newest events are dropped and counted instead of making the logging code wait or the memory grow.
	/// </summary>
	/// <param name="sinks">Adds the sinks.</param>
	/// <param name="capacity">How many events the queue holds.</param>
	/// <param name="drops">Receives the count of dropped events.</param>
	/// <returns>The logger.</returns>
	public static Logger Create(Action<LoggerSinkConfiguration> sinks, int capacity, LogDrops drops)
	{
		return new LoggerConfiguration().MinimumLevel.Verbose()
			.WriteTo.Async(sinks, capacity, false, drops).CreateLogger();
	}

	/// <summary>A sink that creates the real sink when the first event arrives.</summary>
	private sealed class LazySink(Func<Logger> create) : ILogEventSink, IDisposable
	{
		private readonly Lazy<Logger> _logger = new(create);

		public void Dispose()
		{
			if (_logger.IsValueCreated) _logger.Value.Dispose();
		}

		public void Emit(LogEvent logEvent)
		{
			_logger.Value.Write(logEvent);
		}
	}
}