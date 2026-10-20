using Basil.Domain.Mechanics;

namespace Basil.Application.Storage.Beatmaps;

/// <summary>Reads what a beatmapset archive contains.</summary>
public interface IBeatmapsetReader
{
	/// <summary>Reads a beatmapset archive.</summary>
	/// <param name="archive">The .osz archive, read from its current position; not disposed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     What the archive contains, or <see langword="null" /> when it is not a readable beatmapset archive: not an
	///     archive, no readable difficulty, a file named outside the set or named twice, or too many or too large files.
	/// </returns>
	/// <remarks>The content's files are read from the archive, which must stay open until the content is disposed.</remarks>
	Task<BeatmapsetContent?> ReadAsync(Stream archive, CancellationToken cancellationToken = default);
}

/// <summary>What a beatmapset contains.</summary>
/// <param name="onlineSetId">
///     The beatmapset id the difficulties declare, or <see langword="null" /> when they declare
///     none.
/// </param>
/// <param name="artist">The artist.</param>
/// <param name="title">The title.</param>
/// <param name="creator">The mapper's name.</param>
/// <param name="difficulties">The difficulties.</param>
/// <param name="files">Every file of the set, the difficulties' files included.</param>
/// <param name="storyboard">The set's storyboard file, or <see langword="null" /> when it has none.</param>
/// <param name="source">What the files are read from, released when the content is disposed; or <see langword="null" />.</param>
/// <remarks>The files stay readable until the content is disposed.</remarks>
public sealed class BeatmapsetContent(
	int? onlineSetId,
	string artist,
	string title,
	string creator,
	IReadOnlyList<BeatmapsetDifficulty> difficulties,
	IReadOnlyList<BeatmapsetContentFile> files,
	BeatmapsetContentFile? storyboard,
	IAsyncDisposable? source) : IAsyncDisposable
{
	/// <summary>Gets the beatmapset id the difficulties declare, or <see langword="null" /> when they declare none.</summary>
	public int? OnlineSetId => onlineSetId;

	/// <summary>Gets the artist.</summary>
	public string Artist => artist;

	/// <summary>Gets the title.</summary>
	public string Title => title;

	/// <summary>Gets the mapper's name.</summary>
	public string Creator => creator;

	/// <summary>Gets the difficulties.</summary>
	public IReadOnlyList<BeatmapsetDifficulty> Difficulties => difficulties;

	/// <summary>Gets every file of the set, the difficulties' files included.</summary>
	public IReadOnlyList<BeatmapsetContentFile> Files => files;

	/// <summary>Gets the set's storyboard file, or <see langword="null" /> when it has none.</summary>
	public BeatmapsetContentFile? Storyboard => storyboard;

	/// <summary>Releases what the files are read from; the files cannot be read afterwards.</summary>
	/// <returns>A task that completes when the source is released.</returns>
	public ValueTask DisposeAsync()
	{
		return source?.DisposeAsync() ?? ValueTask.CompletedTask;
	}
}

/// <summary>One difficulty of a beatmapset.</summary>
/// <param name="OnlineId">The beatmap id the difficulty declares, or <see langword="null" /> when it declares none.</param>
/// <param name="Version">The difficulty name.</param>
/// <param name="Mode">The game mode.</param>
/// <param name="File">The difficulty's .osu file.</param>
/// <param name="Background">The file of its background, or <see langword="null" /> when it names none the set has.</param>
/// <param name="Audio">The file of its audio track, or <see langword="null" /> when it names none the set has.</param>
/// <param name="Video">The file of its video, or <see langword="null" /> when it names none the set has.</param>
/// <param name="PreviewTime">Where its audio preview starts, or <see langword="null" /> when it sets none.</param>
/// <param name="Storyboard">Whether the difficulty draws a storyboard of its own.</param>
public sealed record BeatmapsetDifficulty(
	int? OnlineId,
	string Version,
	GameMode Mode,
	BeatmapsetContentFile File,
	BeatmapsetContentFile? Background,
	BeatmapsetContentFile? Audio,
	BeatmapsetContentFile? Video,
	TimeSpan? PreviewTime,
	bool Storyboard);