using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Beatmaps.Logic;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class SearchBeatmapsetsDirectHandler(ISender sender, TimeProvider clock)
	: IRequestHandler<SearchBeatmapsetsDirect,
		(BeatmapDirectResult Result, Page<BeatmapsetRecord> Page, IReadOnlyList<BeatmapsetDetail> Details)>
{
	public async ValueTask<(BeatmapDirectResult Result, Page<BeatmapsetRecord> Page, IReadOnlyList<BeatmapsetDetail> Details)>
		Handle(SearchBeatmapsetsDirect request, CancellationToken ct)
	{
		if (!BeatmapVisibility.MayUseDirect(request.Actor, clock)) return (BeatmapDirectResult.Denied, default!, []);

		var page = await sender.Send(new SearchBeatmapsets(request.Actor, request.Query, request.Page), ct);
		var details = await sender.Send(new GetBeatmapsets(request.Actor, [.. page.Items.Select(set => set.Id)]), ct);
		return (BeatmapDirectResult.Ok, page, details);
	}
}