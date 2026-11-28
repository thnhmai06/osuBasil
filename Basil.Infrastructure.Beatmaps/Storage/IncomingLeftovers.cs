using Basil.Infrastructure.Shared.Registry;
using Basil.Infrastructure.Shared.Storage;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Beatmaps.Storage;

internal sealed class IncomingLeftovers(DataPaths paths, ILogger<IncomingLeftovers> logger) : IResident
{
	public Task LoadAsync(CancellationToken ct)
	{
		foreach (var folder in Directory.EnumerateDirectories(paths.Beatmapsets.Path)
			         .Where(folder => FileBeatmapsetNames.IsHidden(Path.GetFileName(folder))))
			DeleteLeftover(() => Directory.Delete(folder, true));
		foreach (var file in Directory.EnumerateFiles(paths.Beatmapsets.Path)
			         .Where(file => FileBeatmapsetNames.IsHidden(Path.GetFileName(file))))
			DeleteLeftover(() => File.Delete(file));
		return Task.CompletedTask;
	}

	private void DeleteLeftover(Action delete)
	{
		try
		{
			delete();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			logger.LogWarning(exception, "Could not delete a leftover of an unfinished beatmapset import");
		}
	}
}