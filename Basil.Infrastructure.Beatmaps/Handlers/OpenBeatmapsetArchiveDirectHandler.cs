using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Infrastructure.Beatmaps.Logic;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class OpenBeatmapsetArchiveDirectHandler(ISender sender, TimeProvider clock)
	: IRequestHandler<OpenBeatmapsetArchiveDirect, (BeatmapDirectResult Result, Stream? Archive)>
{
	public async ValueTask<(BeatmapDirectResult Result, Stream? Archive)> Handle(OpenBeatmapsetArchiveDirect request,
		CancellationToken ct)
	{
		return BeatmapVisibility.MayUseDirect(request.Actor, clock)
			? (BeatmapDirectResult.Ok,
				await sender.Send(new OpenBeatmapsetArchive(request.Actor, request.SetId, request.WithVideo), ct))
			: (BeatmapDirectResult.Denied, null);
	}
}