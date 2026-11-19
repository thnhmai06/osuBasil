using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

public sealed record UploadBeatmapset(Session Actor, Stream Archive)
	: IRequest<(ServerOutcome Result, BeatmapsetImportResult? Import)>;