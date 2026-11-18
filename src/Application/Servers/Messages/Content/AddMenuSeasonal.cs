using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages;

public sealed record AddMenuSeasonal(Connection Actor, string Name, Stream Image)
	: IRequest<(ServerOutcome Result, ContentReference? Image)>;