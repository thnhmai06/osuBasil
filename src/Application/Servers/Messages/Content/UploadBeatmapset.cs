using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>
///     Asks Servers to upload a beatmapset archive as a new beatmapset; the archive is read from its position and not
///     disposed.
/// </summary>
/// <param name="Actor">The connection acting.</param>
/// <param name="Archive">The beatmapset archive to upload.</param>
public sealed record UploadBeatmapset(Connection Actor, Stream Archive)
	: IRequest<(ServerOutcome Result, BeatmapsetImportResult? Import)>;