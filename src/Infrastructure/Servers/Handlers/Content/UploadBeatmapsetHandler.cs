using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="UploadBeatmapset" /> with the rights of the sending connection.</summary>
internal sealed class UploadBeatmapsetHandler(Server server)
	: IRequestHandler<UploadBeatmapset, (Result<ServerOutcome> Result, BeatmapsetImportResult? Import)>
{
	/// <inheritdoc />
	public async ValueTask<(Result<ServerOutcome> Result, BeatmapsetImportResult? Import)> Handle(
		UploadBeatmapset request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Beatmapsets.UploadAsync(request.Archive, ct);
	}
}