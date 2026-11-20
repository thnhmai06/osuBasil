using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Game;

namespace Basil.Application.Beatmaps.Ports;

public interface IBeatmapsetReader
{
	Task<BeatmapsetContent?> ReadAsync(Stream archive, CancellationToken ct = default);
}

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
	public int? OnlineSetId => onlineSetId;

	public string Artist => artist;

	public string Title => title;

	public string Creator => creator;

	public IReadOnlyList<BeatmapsetDifficulty> Difficulties => difficulties;

	public IReadOnlyList<BeatmapsetContentFile> Files => files;

	public BeatmapsetContentFile? Storyboard => storyboard;

	public ValueTask DisposeAsync()
	{
		return source?.DisposeAsync() ?? ValueTask.CompletedTask;
	}
}

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