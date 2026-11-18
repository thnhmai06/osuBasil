using Basil.Configuration;
using Microsoft.Extensions.Options;

namespace Basil.Logging;

public static class LogRetention
{
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

internal sealed class LogRetentionJob(
	IOptions<LoggingOptions> options,
	TimeProvider time,
	ILogger<LogRetentionJob> logger)
	: BackgroundService
{
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