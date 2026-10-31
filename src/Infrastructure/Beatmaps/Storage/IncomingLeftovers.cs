using Basil.Infrastructure.Common.Registry;
using Basil.Infrastructure.Common.Storage;

namespace Basil.Infrastructure.Beatmaps.Storage;

/// <summary>Removes the names the beatmapset storage was writing or setting aside when the server last stopped.</summary>
internal sealed class IncomingLeftovers(DataPaths paths) : IResident
{
	/// <inheritdoc />
	public Task LoadAsync(CancellationToken cancellationToken)
	{
		foreach (var folder in Directory.EnumerateDirectories(paths.Beatmapsets.Path)
			         .Where(folder => FileBeatmapsetNames.IsHidden(Path.GetFileName(folder))))
			DeleteLeftover(() => Directory.Delete(folder, true));
		foreach (var file in Directory.EnumerateFiles(paths.Beatmapsets.Path)
			         .Where(file => FileBeatmapsetNames.IsHidden(Path.GetFileName(file))))
			DeleteLeftover(() => File.Delete(file));
		return Task.CompletedTask;
	}

	/// <summary>Removes one leftover, leaving what it cannot remove to the next startup.</summary>
	/// <param name="delete">The removal.</param>
	private static void DeleteLeftover(Action delete)
	{
		try
		{
			delete();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}
}