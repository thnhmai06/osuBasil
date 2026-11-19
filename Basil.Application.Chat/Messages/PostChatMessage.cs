using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Chat;
using Mediator;
using Basil.Application.Chat.Objects;

namespace Basil.Application.Chat.Messages;

public sealed record PostChatMessage(Session Actor, ChatTarget Target, string Text, bool Notice = false)
	: IRequest<ChannelJoinResult>;