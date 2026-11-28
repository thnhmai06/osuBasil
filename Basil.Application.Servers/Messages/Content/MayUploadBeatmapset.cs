using Basil.Application.Servers.Objects;
using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

/// <summary>Asks whether a session may upload a beatmapset, so a refused upload is not read at all.</summary>
public sealed record MayUploadBeatmapset(Session Actor) : IRequest<ServerActionResult>;
