using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Beatmaps.Logic;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Opens the archive of a beatmapset for a caller who may see it.</summary>
internal sealed class OpenBeatmapsetArchiveHandler(IBeatmapsetRepository sets, IBeatmapsetStorage storage, TimeProvider clock)
	: IRequestHandler<OpenBeatmapsetArchive, Stream?>
{
	/// <inheritdoc />
	public async ValueTask<Stream?> Handle(OpenBeatmapsetArchive request, CancellationToken ct)
	{
		return await sets.GetAsync(request.SetId, ct) is { } set &&
		       BeatmapVisibility.CanView(set, BeatmapVisibility.MayViewHidden(request.Actor, clock))
			? await storage.OpenArchiveAsync(set, request.WithVideo, ct)
			: null;
	}
}