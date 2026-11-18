using Basil.Application.Chat.Objects;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Chat.Messages;

public sealed record JoinChannel(Connection Actor, ChatTarget Target) : IRequest<ChannelOutcome>;