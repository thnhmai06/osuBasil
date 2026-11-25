using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class GetBeatmapHandler(IBeatmapRepository beatmaps, Beatmapsets beatmapsets)
	: IRequestHandler<GetBeatmap, BeatmapRecord?>
{
	public async ValueTask<BeatmapRecord?> Handle(GetBeatmap request, CancellationToken ct)
	{
		return await beatmaps.GetAsync(request.BeatmapId, ct) is { } map &&
		       beatmapsets.ScopeTo(request.Actor).CanView(map)
			? map
			: null;
	}
}