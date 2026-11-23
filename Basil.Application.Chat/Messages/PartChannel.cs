using Basil.Application.Chat.Objects;
using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Chat.Messages;

public sealed record PartChannel(Session Actor, ChatTarget Target) : IRequest<ChannelJoinResult>;