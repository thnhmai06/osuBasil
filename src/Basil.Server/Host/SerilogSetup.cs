using Basil.Server.Shared.Logging;
using Serilog;
using Serilog.Events;

namespace Basil.Server.Host;

/// <summary>Configures Serilog for the host.</summary>
internal static class SerilogSetup
{
	/// <summary>
	///     The size a rolling log file may reach before a new file is started, in addition to the
	///     daily roll. Left at Serilog's own 1 GB default, a burst of request-volume logging silently
	///     truncates the log with no error and no indication anything was lost -- this happened twice
	///     during the 2026 performance investigation, both times destroying the evidence for the
	///     failure under study. Rolling on size as well as on the day keeps every file small enough to
	///     be one coherent artifact instead of one that stops mid-incident.
	/// </summary>
	private const long FileSizeLimitBytes = 256 * 1024 * 1024;

	/// <summary>
	///     How long a rolled file is kept. Used instead of a file-count limit because, once size
	///     rolling is in play, a fixed file count no longer corresponds to a fixed number of days.
	/// </summary>
	private static readonly TimeSpan RetainedFileTimeLimit = TimeSpan.FromDays(30);

	/// <summary>
	///     Configures Serilog for the host, writing to the console and two rolling file sinks that
	///     roll daily or at <see cref="FileSizeLimitBytes" />, whichever comes first.
	/// </summary>
	/// <remarks>
	///     Console and full-file output honor <c>Basil:Logging:MinimumLevel</c> (default
	///     Information); the error file always stays Error-only regardless of that setting.
	/// </remarks>
	/// <param name="builder">The web application builder whose logging configuration is set up.</param>
	public static void Configure(WebApplicationBuilder builder)
	{
		var logsPath = Path.Combine(AppContext.BaseDirectory, "Logs");
		const string template =
			"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{Category}] {RequestId} {SourceContext}: {Message:lj} {Properties}{NewLine}{Exception}";

		var minimumLevel = Enum.TryParse<LogEventLevel>(
			builder.Configuration["Basil:Logging:MinimumLevel"], true, out var configuredLevel)
			? configuredLevel
			: LogEventLevel.Information;

		builder.Services.AddSerilog(lc => lc
			.Enrich.FromLogContext()
			.Enrich.With<CategoryEnricher>()
			.MinimumLevel.Is(minimumLevel)
			.MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
			// Anything the CategoryEnricher couldn't place in a curated scope (Beatmapsets/Matches/Scores/
			// Online/IRC/Host/Database/Cache) is generic framework/library chatter, not a domain event
			// worth Information-level noise; only Warning+ from it shows by default. Curated categories,
			// and anything at Warning+ regardless of category, are never touched here.
			.Filter.ByExcluding(e =>
				e.Level < LogEventLevel.Warning &&
				e.Properties.TryGetValue("Category", out var category) &&
				category is ScalarValue { Value: CategoryEnricher.FallbackCategory })
			// Both sinks are async-wrapped so a stalled console pipe or slow disk can never block the
			// thread that produced the log event: the event goes onto a bounded in-memory queue drained
			// by one background thread, and -- blockWhenFull left at its default false -- a queue that
			// fills is relieved by dropping the newest event rather than blocking the caller.
			.WriteTo.Async(a => a.Console(outputTemplate: template))
			.WriteTo.Async(a => a.File(
				Path.Combine(logsPath, "full", "basil-.log"),
				rollingInterval: RollingInterval.Day,
				fileSizeLimitBytes: FileSizeLimitBytes,
				rollOnFileSizeLimit: true,
				retainedFileCountLimit: null,
				retainedFileTimeLimit: RetainedFileTimeLimit,
				outputTemplate: template,
				hooks: new HardLinkFileLifecycleHooks(Path.Combine(logsPath, "latest.log"))))
			.WriteTo.Async(a => a.File(
				Path.Combine(logsPath, "errors", "basil-.log"),
				LogEventLevel.Error,
				rollingInterval: RollingInterval.Day,
				fileSizeLimitBytes: FileSizeLimitBytes,
				rollOnFileSizeLimit: true,
				retainedFileCountLimit: null,
				retainedFileTimeLimit: RetainedFileTimeLimit,
				outputTemplate: template,
				hooks: new HardLinkFileLifecycleHooks(Path.Combine(logsPath, "errors_latest.log")))));
	}
}
