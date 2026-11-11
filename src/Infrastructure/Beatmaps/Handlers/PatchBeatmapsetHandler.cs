using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Changes a beatmapset with the privileges of the sending connection.</summary>
internal sealed class PatchBeatmapsetHandler(Beatmapsets beatmapsets)
	: IRequestHandler<PatchBeatmapset, BeatmapsetOutcome>
{
	/// <inheritdoc />
	public async ValueTask<BeatmapsetOutcome> Handle(PatchBeatmapset request,
		CancellationToken ct)
	{
		var scoped = beatmapsets.ScopeTo(request.Actor, request.SetId);
		if (request.Status is { } status && await scoped.SetStatusAsync(status) is var a and not BeatmapsetOutcome.Ok)
			return a;
		if (request.Visible is { } visible &&
		    await scoped.SetVisibleAsync(visible) is var b and not BeatmapsetOutcome.Ok) return b;
		if (request.Locked is { } locked && await scoped.SetLockedAsync(locked) is var c and not BeatmapsetOutcome.Ok)
			return c;
		return BeatmapsetOutcome.Ok;
	}
}