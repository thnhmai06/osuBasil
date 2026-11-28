using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Shared.Storage;
using Basil.Infrastructure.Shared.Storage;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class OpenBeatmapsetCoverHandler(
	IBeatmapsetRepository sets,
	IBeatmapRepository beatmaps,
	IBeatmapsetStorage storage)
	: IRequestHandler<OpenBeatmapsetCover, ImageFile?>
{
	public async ValueTask<ImageFile?> Handle(OpenBeatmapsetCover request, CancellationToken ct)
	{
		if (await sets.GetAsync(request.SetId, ct) is not { Visible: true } set) return null;

		var map = (await beatmaps.ListAsync(set, ct)).OrderBy(b => b.Id)
			.FirstOrDefault(b => b is { Visible: true, Assets.BackgroundFile: not null });
		return map is null
			? null
			: await ImageFiles.DescribeAsync(
				await storage.OpenCoverAsync(set, map.Assets.BackgroundFile!, request.Variant, ct), ct);
	}
}
