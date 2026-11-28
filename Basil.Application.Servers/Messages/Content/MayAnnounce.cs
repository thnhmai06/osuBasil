using Basil.Application.Servers.Objects;
using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

/// <summary>Asks whether a session may announce to users, so a refused announcement is not read at all.</summary>
public sealed record MayAnnounce(Session Actor) : IRequest<ServerActionResult>;
