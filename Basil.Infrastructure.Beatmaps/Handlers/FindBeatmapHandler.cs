using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class FindBeatmapHandler(IBeatmapRepository beatmaps) : IRequestHandler<FindBeatmap, BeatmapRecord?>
{
	public async ValueTask<BeatmapRecord?> Handle(FindBeatmap request, CancellationToken ct)
	{
		return await beatmaps.GetAsync(request.BeatmapId, ct);
	}
}