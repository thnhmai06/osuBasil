using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record UploadBeatmapset(Connection Actor, Stream Archive)
	: IRequest<(ServerOutcome Result, BeatmapsetImportResult? Import)>;