using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class SearchBeatmapsetsHandler(IBeatmapsetRepository sets, Beatmapsets beatmapsets)
	: IRequestHandler<SearchBeatmapsets, Page<BeatmapsetRecord>>
{
	public async ValueTask<Page<BeatmapsetRecord>> Handle(SearchBeatmapsets request, CancellationToken ct)
	{
		return await sets.ListAsync(beatmapsets.ScopeTo(request.Actor).Limit(request.Query), request.Page, ct);
	}
}