using Basil.Configuration;
using Microsoft.Extensions.Options;

namespace Basil.Logging;

/// <summary>Deletes the log files that are too old, too many or too large together.</summary>
public static class LogRetention
{
	/// <summary>Deletes the log files of a folder that go beyond the limits, keeping the newest.</summary>
	/// <param name="directory">The folder of the log files.</param>
	/// <param name="maxAge">How long a log file is kept after its last write; zero for no limit.</param>
	/// <param name="maxFiles">How many log files are kept; zero for no limit.</param>
	/// <param name="maxBytes">The total size of the kept log files; zero for no limit. The newest file is always kept.</param>
	/// <param name="now">The current time.</param>
	/// <returns>How many files were deleted.</returns>
	public static int Sweep(string directory, TimeSpan maxAge, int maxFiles, long maxBytes, DateTimeOffset now)
	{
		if (!Directory.Exists(directory)) return 0;

		var deleted = 0;
		var kept = 0;
		long keptBytes = 0;
		foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.log")
			         .OrderByDescending(file => file.LastWriteTimeUtc))
		{
			var excess = (maxAge > TimeSpan.Zero && file.LastWriteTimeUtc < now - maxAge) ||
			             (maxFiles > 0 && kept >= maxFiles) ||
			             (maxBytes > 0 && kept > 0 && keptBytes + file.Length > maxBytes);
			if (!excess)
			{
				kept++;
				keptBytes += file.Length;
				continue;
			}

			try
			{
				file.Delete();
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
internal sealed class LogRetentionJob(
	IOptions<LoggingOptions> options,
	TimeProvider time,
	ILogger<LogRetentionJob> logger)
	: BackgroundService
{
	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromHours(6), time);
		do
		{
			try
			{
				var file = options.Value.File;
				var deleted = LogRetention.Sweep(ConfigurationSetup.Resolve(file.Location), file.Retention.MaxAge,
					file.Retention.MaxFiles, ByteSize.Parse(file.Retention.MaxSize, "Logging:File:Retention:MaxSize"),
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