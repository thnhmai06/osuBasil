using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Beatmaps.Logic;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Opens the audio, video or background of a beatmap for a caller who may see it.</summary>
internal sealed class OpenBeatmapFileHandler(
	IBeatmapRepository beatmaps,
	IBeatmapsetStorage storage,
	TimeProvider clock)
	: IRequestHandler<OpenBeatmapFile, BeatmapFile?>
{
	/// <inheritdoc />
	public async ValueTask<BeatmapFile?> Handle(OpenBeatmapFile request, CancellationToken ct)
	{
		if (await beatmaps.GetAsync(request.BeatmapId, ct) is not { } map ||
		    !BeatmapVisibility.CanView(map, BeatmapVisibility.MayViewHidden(request.Actor, clock)) ||
		    FileOf(map, request.Type) is not { } name)
			return null;

		return await storage.OpenAsync(map.Beatmapset, request.Type, name, ct) is { } content
			? new BeatmapFile(content, name)
			: null;
	}

	/// <summary>Gets the name of the file a beatmap keeps for a kind of file.</summary>
	internal static string? FileOf(BeatmapRecord map, BeatmapsetFileType type)
	{
		return type switch
		{
			BeatmapsetFileType.Audio or BeatmapsetFileType.Preview => map.Assets.AudioFile,
			BeatmapsetFileType.Video => map.Assets.VideoFile,
			BeatmapsetFileType.Background => map.Assets.BackgroundFile,
			_ => null
		};
	}
}