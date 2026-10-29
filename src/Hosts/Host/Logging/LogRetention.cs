using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Basil.Host.Logging;

/// <summary>Deletes the log files that nobody has written to for longer than the retention period.</summary>
public static class LogRetention
{
	/// <summary>Deletes the old log files of a folder.</summary>
	/// <param name="directory">The folder of the log files.</param>
	/// <param name="retention">How long a log file is kept after its last write.</param>
	/// <param name="now">The current time.</param>
	/// <returns>How many files were deleted.</returns>
	public static int Sweep(string directory, TimeSpan retention, DateTimeOffset now)
	{
		if (!Directory.Exists(directory)) return 0;

		var deleted = 0;
		foreach (var file in Directory.EnumerateFiles(directory, "*.log"))
		{
			if (File.GetLastWriteTimeUtc(file) >= now - retention) continue;

			try
			{
				File.Delete(file);
				deleted++;
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				// A file that is still open or protected is tried again at the next sweep.
			}
		}

		return deleted;
	}
}

/// <summary>Runs the log retention when the server starts and then every six hours.</summary>
internal sealed class LogRetentionJob(IOptionsMonitor<LoggingOptions> options, TimeProvider time, ILogger<LogRetentionJob> logger)
	: BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromHours(6), time);
		do
		{
			try
			{
				var current = options.CurrentValue;
				var deleted = LogRetention.Sweep(ConfigurationSetup.Resolve(current.Directory), current.RetentionPeriod,
					time.GetUtcNow());
				if (deleted > 0) logger.LogInformation("Deleted {FileCount} log files past their retention", deleted);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				logger.LogError(exception, "Could not delete old log files");
			}
		} while (await timer.WaitForNextTickAsync(stoppingToken));
	}
}
