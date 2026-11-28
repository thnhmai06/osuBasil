using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Persistence;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class GetBeatmapsetHandler(IBeatmapsetRepository sets, IBeatmapRepository beatmaps, Beatmapsets beatmapsets)
	: IRequestHandler<GetBeatmapset, BeatmapsetDetail?>
{
	public async ValueTask<BeatmapsetDetail?> Handle(GetBeatmapset request, CancellationToken ct)
	{
		var scope = beatmapsets.ScopeTo(request.Actor);
		if (await sets.GetAsync(request.SetId, ct) is not { } set || !scope.CanView(set))
			return null;

		var list = await beatmaps.ListAsync(set, ct);
		return new BeatmapsetDetail(set,
			[.. list.Where(map => scope.CanView(map)).OrderBy(map => map.Id)]);
	}
}