using System.Globalization;
using Basil.Application.Services.Contracts.Beatmaps;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Beatmaps;

internal static class BeatmapArchiveImporter
{
	internal static async Task<ImportStatus> ImportAsync(
		string path,
		IBeatmapsetService beatmapsets,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		FileStream archive;
		try
		{
			archive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
		}
		catch (FileNotFoundException)
		{
			return ImportStatus.Missing;
		}
		catch (DirectoryNotFoundException)
		{
			return ImportStatus.Missing;
		}
		catch (IOException exception)
		{
			logger.LogDebug(exception, "Beatmap archive {Path} is not ready to read.", path);
			return ImportStatus.Locked;
		}

		BeatmapsetImportResult result;
		try
		{
			await using (archive)
			{
				result = await beatmapsets.ImportAsync(archive, IdFromFileName(path), cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Could not import beatmap archive {Path}; the file was kept.", path);
			return ImportStatus.Failed;
		}

		if (result is not { Failure: null, Set: not null })
		{
			logger.LogWarning("Could not import beatmap archive {Path}: {Failure}; the file was kept.", path,
				result.Failure?.ToString() ?? "No beatmapset was returned.");
			return ImportStatus.Failed;
		}

		try
		{
			File.Delete(path);
			logger.LogInformation("Imported beatmap archive {Path}.", path);
			return ImportStatus.Imported;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			logger.LogError(exception, "Imported beatmap archive {Path}, but could not delete the file.", path);
			return ImportStatus.Failed;
		}
	}

	private static int? IdFromFileName(string path)
	{
		var name = Path.GetFileNameWithoutExtension(path).AsSpan();
		var length = 0;
		while (length < name.Length && char.IsAsciiDigit(name[length])) length++;

		return length > 0 && int.TryParse(name[..length], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
			? id
			: null;
	}

	internal enum ImportStatus
	{
		Imported,
		Failed,
		Locked,
		Missing
	}
}