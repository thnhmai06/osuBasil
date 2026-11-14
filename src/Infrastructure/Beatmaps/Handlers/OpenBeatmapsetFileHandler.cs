using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Infrastructure.Beatmaps.Logic;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Opens the audio preview or the storyboard of a beatmapset for a caller who may see it.</summary>
internal sealed class OpenBeatmapsetFileHandler(
	IBeatmapsetRepository sets,
	IBeatmapRepository beatmaps,
	IBeatmapsetStorage storage,
	TimeProvider clock) : IRequestHandler<OpenBeatmapsetFile, BeatmapFile?>
{
	// shortcut: the stored name is the fixed storyboard name of the importer; read it from there if it ever varies.
	private const string StoryboardName = "storyboard.osb";

	/// <inheritdoc />
	public async ValueTask<BeatmapFile?> Handle(OpenBeatmapsetFile request, CancellationToken ct)
	{
		var mayViewHidden = BeatmapVisibility.MayViewHidden(request.Actor, clock);
		if (await sets.GetAsync(request.SetId, ct) is not { } set || !BeatmapVisibility.CanView(set, mayViewHidden))
			return null;

		var name = request.Type == BeatmapsetFileType.Storyboard
			? set.Assets.HasStoryboard ? StoryboardName : null
			: (await beatmaps.ListAsync(set, ct)).OrderBy(map => map.Id)
			.Where(map => BeatmapVisibility.CanView(map, mayViewHidden))
			.Select(map => OpenBeatmapFileHandler.FileOf(map, request.Type)).FirstOrDefault(file => file is not null);
		return name is not null && await storage.OpenAsync(set, request.Type, name, ct) is { } content
			? new BeatmapFile(content, name)
			: null;
	}
}