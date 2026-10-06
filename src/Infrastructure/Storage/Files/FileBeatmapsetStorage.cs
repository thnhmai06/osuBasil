using System.Text;
using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Domain.Beatmaps;

namespace Basil.Infrastructure.Storage.Files;

/// <summary>Stores the <c>.osz</c> archives of beatmapsets, one file per set, named after the set.</summary>
/// <remarks>
///     A set's archive is located by the leading id of its file name, so it is found and replaced even
///     when the set's artist or title no longer match the name the file was stored under.
/// </remarks>
internal sealed class FileBeatmapsetStorage(DataPaths paths) : IBeatmapsetStorage
{
	/// <inheritdoc />
	public async Task SaveAsync(Beatmapset set, Stream content, CancellationToken cancellationToken = default)
	{
		var target = Path.Combine(paths.Beatmapsets, Name(set));
		await FileStorage.SaveAsync(target, content, cancellationToken);
		FileStorage.DeleteExcept(paths.Beatmapsets, $"{set.Id} *.osz", target);
	}

	/// <inheritdoc />
	public Task<Stream?> OpenAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		var files = FileStorage.Files(paths.Beatmapsets, $"{set.Id} *.osz");
		var path = files is null ? null : files.FirstOrDefault();
		return Task.FromResult<Stream?>(path is null ? null : FileStorage.Open(path));
	}

	/// <inheritdoc />
	public Task DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		var files = FileStorage.Files(paths.Beatmapsets, $"{set.Id} *.osz");
		if (files is not null)
		{
			foreach (var file in files)
				File.Delete(file);
		}

		return Task.CompletedTask;
	}

	/// <summary>Builds the name of a beatmapset's archive file.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <returns>The file name, with invalid file-name characters replaced by underscores.</returns>
	private static string Name(Beatmapset set)
	{
		return Sanitize($"{set.Id} {set.Value.Artist} - {set.Value.Title}") + ".osz";
	}

	/// <summary>Replaces the invalid file-name characters of a name and cuts it to 200 characters.</summary>
	/// <param name="name">The name to sanitize.</param>
	/// <returns>The sanitized name, at most 200 characters.</returns>
	private static string Sanitize(string name)
	{
		var builder = new StringBuilder(name.Length);
		foreach (var character in name)
			builder.Append(Path.GetInvalidFileNameChars().Contains(character) ? '_' : character);

		return builder.ToString(0, Math.Min(builder.Length, 200));
	}
}
