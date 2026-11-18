using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record SetMenuIcon(Connection Actor, string? Name, Stream? Image, Uri? Url)
	: IRequest<ServerOutcome>;