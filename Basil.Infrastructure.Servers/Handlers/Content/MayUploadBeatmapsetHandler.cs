using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class MayUploadBeatmapsetHandler(Server server) : IRequestHandler<MayUploadBeatmapset, ServerActionResult>
{
	public async ValueTask<ServerActionResult> Handle(MayUploadBeatmapset request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Beatmapsets.MayUploadAsync(ct);
	}
}
