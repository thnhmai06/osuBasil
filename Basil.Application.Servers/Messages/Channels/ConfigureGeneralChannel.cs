using Basil.Application.Chat.Objects;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Chat;
using Mediator;

namespace Basil.Application.Servers.Messages.Channels;

public sealed record ConfigureGeneralChannel(Session Actor, string Name, GeneralChannelChange Change)
	: IRequest<GeneralChannelAnswer>;

public sealed record GeneralChannelAnswer(ChannelJoinResult Result, GeneralChannel? Channel);