using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Beatmaps;
using Basil.Application.Common;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Answers <see cref="SearchBeatmapsets" /> from the stored beatmapsets.</summary>
internal sealed class SearchBeatmapsetsHandler(IBeatmapsetRepository sets, TimeProvider clock)
	: IRequestHandler<SearchBeatmapsets, Page<BeatmapsetRecord>>
{
	/// <inheritdoc />
	public async ValueTask<Page<BeatmapsetRecord>> Handle(SearchBeatmapsets request, CancellationToken ct)
	{
		var effective = request.Actor.EffectivePrivileges(clock.GetUtcNow());
		return await sets.ListAsync(request.Query.LimitTo(effective), request.Page, ct);
	}
}