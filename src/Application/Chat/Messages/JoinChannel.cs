using Basil.Application.Chat.Objects;
using Basil.Application.Shared;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Chat.Messages;

/// <summary>Asks Chat to add a connection to a channel.</summary>
/// <param name="Actor">The connection that joins.</param>
/// <param name="Target">The channel to join.</param>
/// <remarks>The answer is <see cref="ChannelOutcome.NotFound" /> when the target is not open.</remarks>
public sealed record JoinChannel(Connection Actor, ChatTarget Target) : IRequest<ChannelOutcome>;