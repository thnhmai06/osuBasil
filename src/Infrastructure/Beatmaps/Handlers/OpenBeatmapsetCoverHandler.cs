using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Opens the cover picture of a visible beatmapset.</summary>
internal sealed class OpenBeatmapsetCoverHandler(
	IBeatmapsetRepository sets,
	IBeatmapRepository beatmaps,
	IBeatmapsetStorage storage)
	: IRequestHandler<OpenBeatmapsetCover, Stream?>
{
	/// <inheritdoc />
	public async ValueTask<Stream?> Handle(OpenBeatmapsetCover request, CancellationToken ct)
	{
		if (await sets.GetAsync(request.SetId, ct) is not { Visible: true } set) return null;

		var map = (await beatmaps.ListAsync(set, ct)).OrderBy(b => b.Id)
			.FirstOrDefault(b => b is { Visible: true, Assets.BackgroundFile: not null });
		return map is null
			? null
			: await storage.OpenAsync(set, BeatmapsetFileType.Background, map.Assets.BackgroundFile!, ct);
	}
}