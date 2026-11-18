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

public sealed class LogDrops : IAsyncLogEventSinkMonitor
{
	private volatile IAsyncLogEventSinkInspector? _inspector;
	private long _stopped;

	public int Pending => _inspector?.Count ?? 0;

	public long Dropped => _inspector?.DroppedMessagesCount ?? Interlocked.Read(ref _stopped);

	public void StartMonitoring(IAsyncLogEventSinkInspector inspector)
	{
		_inspector = inspector;
	}

	public void StopMonitoring(IAsyncLogEventSinkInspector inspector)
	{
		Interlocked.Exchange(ref _stopped, inspector.DroppedMessagesCount);
		_inspector = null;
	}
}

public static class ServerLogging
{
	private const string Template =
		"{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";

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

	public static Logger Create(Action<LoggerSinkConfiguration> sinks, int capacity, LogDrops drops)
	{
		return new LoggerConfiguration().MinimumLevel.Verbose()
			.WriteTo.Async(sinks, capacity, false, drops).CreateLogger();
	}

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