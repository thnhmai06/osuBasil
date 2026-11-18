using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Chat;
using Mediator;

namespace Basil.Application.Servers.Messages.Channels;

public sealed record CreateGeneralChannel(Connection Actor, GeneralChannelRecord Record)
	: IRequest<ServerOutcome>;