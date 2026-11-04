using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Common;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Deletes a beatmapset with the rights of the sending connection.</summary>
internal sealed class DeleteBeatmapsetHandler(Beatmapsets beatmapsets)
	: IRequestHandler<DeleteBeatmapset, Result<BeatmapsetOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<BeatmapsetOutcome>> Handle(DeleteBeatmapset request,
		CancellationToken cancellationToken)
	{
		return await beatmapsets.Of(request.Set).ScopeTo(request.Actor).DeleteAsync(cancellationToken);
	}
}
