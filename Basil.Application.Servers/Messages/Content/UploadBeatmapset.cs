using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Servers.Objects;
using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

public sealed record UploadBeatmapset(Session Actor, Stream Archive)
	: IRequest<(ServerActionResult Result, BeatmapsetImportResult? Import)>;