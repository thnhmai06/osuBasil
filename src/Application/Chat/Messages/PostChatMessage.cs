using Basil.Application.Chat.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Chat.Messages;

public sealed record PostChatMessage(Connection Actor, ChatTarget Target, string Text, bool Notice = false)
	: IRequest<ChannelOutcome>;