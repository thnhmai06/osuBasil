using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class OpenBeatmapsetFileHandler(
	IBeatmapsetRepository sets,
	IBeatmapRepository beatmaps,
	IBeatmapsetStorage storage,
	Beatmapsets beatmapsets) : IRequestHandler<OpenBeatmapsetFile, BeatmapFile?>
{
	// shortcut: the stored name is the fixed storyboard name of the importer; read it from there if it ever varies.
	private const string StoryboardName = "storyboard.osb";

	public async ValueTask<BeatmapFile?> Handle(OpenBeatmapsetFile request, CancellationToken ct)
	{
		var scope = beatmapsets.ScopeTo(request.Actor);
		if (await sets.GetAsync(request.SetId, ct) is not { } set || !scope.CanView(set))
			return null;

		var name = request.Type == BeatmapsetFileType.Storyboard
			? set.Assets.HasStoryboard ? StoryboardName : null
			: (await beatmaps.ListAsync(set, ct)).OrderBy(map => map.Id)
			.Where(map => scope.CanView(map))
			.Select(map => OpenBeatmapFileHandler.FileOf(map, request.Type)).FirstOrDefault(file => file is not null);
		return name is not null && await storage.OpenAsync(set, request.Type, name, ct) is { } content
			? new BeatmapFile(content, name)
			: null;
	}
}