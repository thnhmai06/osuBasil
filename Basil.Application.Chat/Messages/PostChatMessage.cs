using Basil.Application.Chat.Objects;
using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Chat.Messages;

public sealed record PostChatMessage(Session Actor, ChatTarget Target, string Text, bool Notice = false)
	: IRequest<ChannelJoinResult>;