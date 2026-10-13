using Basil.Application.Services.Contracts.Beatmaps;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Beatmaps;

/// <summary>Imports one waiting beatmapset: an archive is deleted once imported, a folder is kept.</summary>
internal static class BeatmapsetImporter
{
	/// <summary>Imports the beatmapset archive at the path and deletes it once imported.</summary>
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
			logger.LogDebug(exception, "Beatmap archive {Path} is not ready to read", path);
			return ImportStatus.Locked;
		}

		BeatmapsetImportResult result;
		try
		{
			await using (archive)
			{
				result = await beatmapsets.ImportAsync(archive, cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Could not import beatmap archive {Path}; the file was kept", path);
			return ImportStatus.Failed;
		}


		if (result is not { Failure: null, Set: not null })
		{
			logger.LogWarning("Could not import beatmap archive {Path}: {Failure}; the file was kept", path,
				result.Failure?.ToString() ?? "No beatmapset was returned.");
			return ImportStatus.Failed;
		}

		try
		{
			File.Delete(path);
			logger.LogInformation("Imported beatmap archive {Path}", path);
			return ImportStatus.Imported;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			logger.LogError(exception, "Imported beatmap archive {Path}, but could not delete the file", path);
			return ImportStatus.Failed;
		}
	}

	/// <summary>Imports the beatmapset files in the folder at the path; the folder is left as it is.</summary>
	internal static async Task<ImportStatus> ImportFolderAsync(
		string path,
		IBeatmapsetService beatmapsets,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		if (!Directory.Exists(path)) return ImportStatus.Missing;

		BeatmapsetImportResult result;
		try
		{
			result = await beatmapsets.ImportAsync(new DirectoryInfo(path), cancellationToken)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Could not import beatmap folder {Path}", path);
			return ImportStatus.Failed;
		}

		if (result is not { Failure: null, Set: not null })
		{
			logger.LogWarning("Could not import beatmap folder {Path}: {Failure}", path,
				result.Failure?.ToString() ?? "No beatmapset was returned.");
			return ImportStatus.Failed;
		}

		logger.LogInformation("Imported beatmap folder {Path}", path);
		return ImportStatus.Imported;
	}

	/// <summary>The outcome of importing one waiting beatmapset.</summary>
	internal enum ImportStatus
	{
		/// <summary>The beatmapset was imported; its archive was deleted, its folder kept.</summary>
		Imported,

		/// <summary>The import failed and the archive or folder was kept.</summary>
		Failed,

		/// <summary>The archive is in use by another process and the import was postponed.</summary>
		Locked,

		/// <summary>The archive or folder is gone.</summary>
		Missing
	}
}
