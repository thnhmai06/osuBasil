using Basil.Application.Chat.Objects;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Servers.Messages.Channels;

public sealed record ConfigureGeneralChannel(Session Actor, string Name, GeneralChannelChange Change)
	: IRequest<GeneralChannelAnswer>;

public sealed record GeneralChannelAnswer(ChannelOutcome Result, GeneralChannel? Channel);