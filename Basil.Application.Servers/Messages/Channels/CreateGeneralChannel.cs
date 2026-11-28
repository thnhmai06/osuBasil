using Basil.Application.Servers.Objects;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Chat;
using Mediator;

namespace Basil.Application.Servers.Messages.Channels;

public sealed record CreateGeneralChannel(Session Actor, GeneralChannelRecord Record)
	: IRequest<ServerActionResult>;