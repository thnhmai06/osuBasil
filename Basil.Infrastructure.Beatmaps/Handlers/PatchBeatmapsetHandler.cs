using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class PatchBeatmapsetHandler(Beatmapsets beatmapsets)
	: IRequestHandler<PatchBeatmapset, BeatmapsetEditResult>
{
	public async ValueTask<BeatmapsetEditResult> Handle(PatchBeatmapset request,
		CancellationToken ct)
	{
		var scoped = beatmapsets.ScopeTo(request.Actor, request.SetId);
		if (request.Status is { } status && await scoped.SetStatusAsync(status) is var a and not BeatmapsetEditResult.Ok)
			return a;
		if (request.Visible is { } visible &&
		    await scoped.SetVisibleAsync(visible) is var b and not BeatmapsetEditResult.Ok) return b;
		if (request.Locked is { } locked && await scoped.SetLockedAsync(locked) is var c and not BeatmapsetEditResult.Ok)
			return c;
		return BeatmapsetEditResult.Ok;
	}
}