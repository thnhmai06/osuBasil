using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Persistence;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class GetBeatmapsetsHandler(IBeatmapsetRepository sets, IBeatmapRepository beatmaps, Beatmapsets beatmapsets)
	: IRequestHandler<GetBeatmapsets, IReadOnlyList<BeatmapsetDetail>>
{
	public async ValueTask<IReadOnlyList<BeatmapsetDetail>> Handle(GetBeatmapsets request, CancellationToken ct)
	{
		var scope = beatmapsets.ScopeTo(request.Actor);
		var found = (await sets.ListAsync(request.SetIds, ct)).ToDictionary(set => set.Id);
		var visible = request.SetIds.Distinct().Where(id => found.TryGetValue(id, out var set) &&
		                                                    scope.CanView(set))
			.Select(id => found[id]).ToList();
		if (visible.Count == 0) return [];

		var byId = (await beatmaps.ListAsync(visible, ct)).ToLookup(map => map.Beatmapset.Id);
		return [.. visible.Select(set => new BeatmapsetDetail(set,
			[.. byId[set.Id].Where(map => scope.CanView(map)).OrderBy(map => map.Id)]))];
	}
}
