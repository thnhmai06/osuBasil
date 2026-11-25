using Basil.Application.Sessions.Objects;
using Basil.Domain.Spectating;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Offers a room a player's score frame during a round, to be shown to everyone who watches the room play.</summary>
/// <param name="Actor">The session of the player.</param>
/// <param name="Room">The room.</param>
/// <param name="Frame">The standing of the player.</param>
public sealed record RelayScore(Session Actor, Room Room, ScoreFrame Frame) : IRequest<bool>;
