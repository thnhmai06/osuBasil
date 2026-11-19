using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class UploadBeatmapsetHandler(Server server)
	: IRequestHandler<UploadBeatmapset, (ServerOutcome Result, BeatmapsetImportResult? Import)>
{
	public async ValueTask<(ServerOutcome Result, BeatmapsetImportResult? Import)> Handle(
		UploadBeatmapset request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Beatmapsets.UploadAsync(request.Archive, ct);
	}
}