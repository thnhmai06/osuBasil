using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Chat;
using Mediator;
using Basil.Application.Chat.Objects;

namespace Basil.Application.Chat.Messages;

public sealed record JoinChannel(Session Actor, ChatTarget Target) : IRequest<ChannelJoinResult>;