using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Common;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Deletes a beatmapset with the privileges of the sending connection.</summary>
internal sealed class DeleteBeatmapsetHandler(Beatmapsets beatmapsets)
	: IRequestHandler<DeleteBeatmapset, BeatmapsetOutcome>
{
	/// <inheritdoc />
	public async ValueTask<BeatmapsetOutcome> Handle(DeleteBeatmapset request,
		CancellationToken ct)
	{
		return await beatmapsets.ScopeTo(request.Actor, request.SetId).DeleteAsync();
	}
}