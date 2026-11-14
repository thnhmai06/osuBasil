using System.Text;
using Basil.Configuration;
using Basil.Host.Cli;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Sinks.Async;
using Serilog.Sinks.SystemConsole.Themes;

namespace Basil.Logging;

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
///     stdout. The levels come from the standard <c>Logging:LogLevel</c> settings, which apply at once.
/// </summary>
public static class ServerLogging
{
	private const string Template =
		"{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";

	/// <summary>Adds the logging to the builder.</summary>
	/// <param name="builder">The builder.</param>
	/// <param name="mode">How the server shows itself in its terminal.</param>
	/// <returns>The counter of dropped events.</returns>
	public static LogDrops Add(WebApplicationBuilder builder, CliOptions mode)
	{
		var options = builder.Configuration.GetSection("Logging").Get<LoggingOptions>() ?? new LoggingOptions();
		var file = options.File;
		var rollingBytes = ByteSize.Parse(file.Rolling.MaxSize, "Logging:File:Rolling:MaxSize");
		ByteSize.Parse(file.Retention.MaxSize, "Logging:File:Retention:MaxSize");
		var telemetry = builder.Configuration.GetSection("OpenTelemetry").Get<TelemetryOptions>() ??
		                new TelemetryOptions();
		var drops = new LogDrops();
		var directory = ConfigurationSetup.Resolve(file.Location);
		var logger = Create(configuration =>
		{
			// The file opens at the first event, so a command run from the shell leaves no empty log behind.
			if (file.Enabled)
				configuration.Sink(new LazySink(() => new LoggerConfiguration().WriteTo.File(
					Path.Combine(directory, "basil-.log"),
					outputTemplate: Template, rollingInterval: file.Rolling.Interval,
					fileSizeLimitBytes: rollingBytes > 0 ? rollingBytes : null, rollOnFileSizeLimit: rollingBytes > 0,
					retainedFileCountLimit: null, encoding: Encoding.UTF8).CreateLogger()));
			if (mode.Console == ConsoleLevel.Log) configuration.Console(outputTemplate: Template, theme: ConsoleTheme.None);
			if (telemetry is { Logs: true, OtlpEndpoint: { Length: > 0 } endpoint })
				configuration.OpenTelemetry(sink => sink.Endpoint = endpoint);
		}, options.QueueCapacity, drops);

		builder.Logging.ClearProviders();
		builder.Logging.SetMinimumLevel(LogLevel.Trace);
		builder.Logging.AddSerilog(logger, true);
		AddLevels(builder);
		builder.Services.AddSingleton(drops);
		builder.Services.AddSingleton(Options.Create(options));
		builder.Services.AddHostedService<LogRetentionJob>();
		return drops;
	}

	/// <summary>
	///     Applies <c>Logging:LogLevel</c> to the logger. Serilog's own rule lets every event through for its provider
	///     and a rule for a provider beats the generic ones, so the levels are set as rules for that provider; they
	///     are read again when the configuration changes.
	/// </summary>
	private static void AddLevels(WebApplicationBuilder builder)
	{
		var levels = builder.Configuration.GetSection("Logging:LogLevel");
		builder.Services.AddSingleton<IOptionsChangeTokenSource<LoggerFilterOptions>>(
			new ConfigurationChangeTokenSource<LoggerFilterOptions>(levels));
		builder.Services.Configure<LoggerFilterOptions>(filter =>
		{
			foreach (var entry in levels.GetChildren())
				if (Enum.TryParse<LogLevel>(entry.Value, true, out var level))
					filter.Rules.Add(new LoggerFilterRule(typeof(SerilogLoggerProvider).FullName,
						entry.Key == "Default" ? null : entry.Key, level, null));
		});
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

		/// <inheritdoc />
		public void Dispose()
		{
			if (_logger.IsValueCreated) _logger.Value.Dispose();
		}

		/// <inheritdoc />
		public void Emit(LogEvent logEvent)
		{
			_logger.Value.Write(logEvent);
		}
	}
}