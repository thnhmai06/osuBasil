using Basil.Application.Services.Contracts.Beatmaps;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Beatmaps;

/// <summary>Imports one waiting beatmapset, from an archive or a folder, and removes it once imported.</summary>
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

		return Complete(path, result, () => File.Delete(path), "archive", "file", logger);
	}

	/// <summary>Imports the beatmapset files in the folder at the path and deletes the folder once imported.</summary>
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
			logger.LogError(exception, "Could not import beatmap folder {Path}; the folder was kept", path);
			return ImportStatus.Failed;
		}

		return Complete(path, result, () => Directory.Delete(path, true), "folder", "folder", logger);
	}

	/// <summary>Checks the import result and deletes the archive or folder, reporting the outcome.</summary>
	private static ImportStatus Complete(string path, BeatmapsetImportResult result, Action delete,
		string kind, string item, ILogger logger)
	{
		if (result is not { Failure: null, Set: not null })
		{
			logger.LogWarning("Could not import beatmap {Kind} {Path}: {Failure}; the {Item} was kept",
				kind, path, result.Failure?.ToString() ?? "No beatmapset was returned.", item);
			return ImportStatus.Failed;
		}

		try
		{
			delete();
			logger.LogInformation("Imported beatmap {Kind} {Path}", kind, path);
			return ImportStatus.Imported;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			logger.LogError(exception, "Imported beatmap {Kind} {Path}, but could not delete the {Item}",
				kind, path, item);
			return ImportStatus.Failed;
		}
	}

	/// <summary>The outcome of importing one waiting beatmapset.</summary>
	internal enum ImportStatus
	{
		/// <summary>The beatmapset was imported and its archive or folder was deleted.</summary>
		Imported,

		/// <summary>The import failed and the archive or folder was kept.</summary>
		Failed,

		/// <summary>The archive is in use by another process and the import was postponed.</summary>
		Locked,

		/// <summary>The archive or folder is gone.</summary>
		Missing
	}
}
