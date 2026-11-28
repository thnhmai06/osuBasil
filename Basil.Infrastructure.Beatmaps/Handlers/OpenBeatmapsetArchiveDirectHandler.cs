using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class OpenBeatmapsetArchiveDirectHandler(ISender sender, Beatmapsets beatmapsets)
	: IRequestHandler<OpenBeatmapsetArchiveDirect, (BeatmapDirectResult Result, Stream? Archive)>
{
	public async ValueTask<(BeatmapDirectResult Result, Stream? Archive)> Handle(OpenBeatmapsetArchiveDirect request,
		CancellationToken ct)
	{
		return beatmapsets.ScopeTo(request.Actor).MayUseDirect()
			? (BeatmapDirectResult.Ok,
				await sender.Send(new OpenBeatmapsetArchive(request.Actor, request.SetId, request.WithVideo), ct))
			: (BeatmapDirectResult.Denied, null);
	}
}