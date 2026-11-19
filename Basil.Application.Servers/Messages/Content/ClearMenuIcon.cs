using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

public sealed record ClearMenuIcon(Session Actor) : IRequest<ServerOutcome>;