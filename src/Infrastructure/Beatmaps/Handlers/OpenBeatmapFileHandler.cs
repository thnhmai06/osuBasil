using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Beatmaps.Logic;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class OpenBeatmapFileHandler(
	IBeatmapRepository beatmaps,
	IBeatmapsetStorage storage,
	TimeProvider clock)
	: IRequestHandler<OpenBeatmapFile, BeatmapFile?>
{
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

	internal static string? FileOf(BeatmapRecord map, BeatmapsetFileType type)
	{
		return type switch
		{
			BeatmapsetFileType.Version => $"{map.Hash}.osu",
			BeatmapsetFileType.Audio or BeatmapsetFileType.Preview => map.Assets.AudioFile,
			BeatmapsetFileType.Video => map.Assets.VideoFile,
			BeatmapsetFileType.Background => map.Assets.BackgroundFile,
			_ => null
		};
	}
}