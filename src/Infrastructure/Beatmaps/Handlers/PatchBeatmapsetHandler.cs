using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Common;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Changes a beatmapset with the rights of the sending connection.</summary>
internal sealed class PatchBeatmapsetHandler(Beatmapsets beatmapsets)
	: IRequestHandler<PatchBeatmapset, Result<BeatmapsetOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<BeatmapsetOutcome>> Handle(PatchBeatmapset request,
		CancellationToken cancellationToken)
	{
		var scoped = beatmapsets.Of(request.Set).ScopeTo(request.Actor);
		if (request.Status is { } status && await scoped.SetStatusAsync(status) is { IsOk: false } a)
			return a;
		if (request.Visible is { } visible &&
		    await scoped.SetVisibleAsync(visible) is { IsOk: false } b) return b;
		if (request.Locked is { } locked && await scoped.SetLockedAsync(locked) is { IsOk: false } c)
			return c;
		return BeatmapsetOutcome.Ok;
	}
}