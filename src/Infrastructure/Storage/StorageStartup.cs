using Basil.Infrastructure.Storage.Beatmaps;
using Basil.Infrastructure.Storage.Common;
using Basil.Infrastructure.Storage.Common.Database;
using Basil.Infrastructure.Storage.Common.Options;
using Microsoft.Extensions.Hosting;

namespace Basil.Infrastructure.Storage;

/// <summary>Prepares the server's storage before anything else uses it.</summary>
internal sealed class StorageStartup(DataPaths paths, DatabaseMigrator migrator, IEnumerable<IResident> residents)
	: IHostedService
{
	/// <summary>
	///     Creates the data directories, removes the folders the last run left behind, brings the database up to date
	///     and loads resident data.
	/// </summary>
	/// <param name="cancellationToken">A token that cancels the startup work.</param>
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		paths.CreateDirectories();
		RemoveLeftovers();
		await migrator.MigrateAsync(cancellationToken);
		foreach (var resident in residents)
			await resident.LoadAsync(cancellationToken);
	}

	/// <summary>Does nothing; the storage needs no shutdown work.</summary>
	/// <param name="cancellationToken">A token that cancels the shutdown work.</param>
	/// <returns>A completed task.</returns>
	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	/// <summary>Removes the set folders that were being removed or written when the server last stopped.</summary>
	private void RemoveLeftovers()
	{
		foreach (var folder in Directory.EnumerateDirectories(paths.Beatmapsets.Path))
			if (Path.GetFileName(folder).Contains(FileBeatmapsetStorage.DeletedMarker, StringComparison.Ordinal))
				DeleteLeftover(folder);
		foreach (var file in Directory.EnumerateFiles(paths.Staging.Path))
			DeleteLeftoverFile(file);
		foreach (var folder in Directory.EnumerateDirectories(paths.Staging.Path))
			DeleteLeftover(folder);

		var formerArchives = Path.Combine(paths.Root, "Cache", "Beatmaps");
		if (Directory.Exists(formerArchives)) DeleteLeftover(formerArchives);
	}

	/// <summary>Removes one leftover file, leaving what it cannot remove to the next startup.</summary>
	/// <param name="file">The file's full path.</param>
	private static void DeleteLeftoverFile(string file)
	{
		try
		{
			File.Delete(file);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}

	/// <summary>Removes one leftover folder, leaving what it cannot remove to the next startup.</summary>
	/// <param name="folder">The folder's full path.</param>
	private static void DeleteLeftover(string folder)
	{
		try
		{
			Directory.Delete(folder, true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}
}