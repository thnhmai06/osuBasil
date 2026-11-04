using Basil.Application.Chat.Objects;
using Basil.Application.Common;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Chat.Messages;

/// <summary>Asks Chat to remove a connection from a channel.</summary>
/// <param name="Actor">The connection that leaves.</param>
/// <param name="Target">The channel to leave.</param>
/// <remarks>The answer is <see cref="ChannelOutcome.NotFound" /> when the target is not open.</remarks>
public sealed record PartChannel(Connection Actor, ChatTarget Target) : IRequest<Result<ChannelOutcome>>;
