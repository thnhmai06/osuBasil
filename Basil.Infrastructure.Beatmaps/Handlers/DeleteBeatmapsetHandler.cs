using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class DeleteBeatmapsetHandler(Beatmapsets beatmapsets)
	: IRequestHandler<DeleteBeatmapset, BeatmapsetEditResult>
{
	public async ValueTask<BeatmapsetEditResult> Handle(DeleteBeatmapset request,
		CancellationToken ct)
	{
		return await beatmapsets.ScopeTo(request.Actor, request.SetId).DeleteAsync();
	}
}