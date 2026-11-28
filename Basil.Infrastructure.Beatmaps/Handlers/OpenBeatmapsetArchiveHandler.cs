using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class OpenBeatmapsetArchiveHandler(
	IBeatmapsetRepository sets,
	IBeatmapsetStorage storage,
	Beatmapsets beatmapsets)
	: IRequestHandler<OpenBeatmapsetArchive, Stream?>
{
	public async ValueTask<Stream?> Handle(OpenBeatmapsetArchive request, CancellationToken ct)
	{
		return await sets.GetAsync(request.SetId, ct) is { } set &&
		       beatmapsets.ScopeTo(request.Actor).CanView(set)
			? await storage.OpenArchiveAsync(set, request.WithVideo, ct)
			: null;
	}
}