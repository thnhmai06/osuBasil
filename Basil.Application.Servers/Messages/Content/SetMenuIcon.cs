using Basil.Application.Servers.Objects;
using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

public sealed record SetMenuIcon(Session Actor, string? Name, Stream? Image, Uri? Url)
	: IRequest<ServerActionResult>;