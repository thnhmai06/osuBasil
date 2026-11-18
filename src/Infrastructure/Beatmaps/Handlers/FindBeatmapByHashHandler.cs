using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class FindBeatmapByHashHandler(IBeatmapRepository beatmaps)
	: IRequestHandler<FindBeatmapByHash, BeatmapRecord?>
{
	public async ValueTask<BeatmapRecord?> Handle(FindBeatmapByHash request, CancellationToken ct)
	{
		return await beatmaps.GetAsync(request.Hash, ct);
	}
}