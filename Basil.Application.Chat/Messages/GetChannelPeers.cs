using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Chat.Messages;

/// <summary>Asks for the sessions of the other users that share a channel with an actor's user.</summary>
/// <param name="Actor">The session asking.</param>
public sealed record GetChannelPeers(Session Actor) : IRequest<IReadOnlyList<Session>>;
