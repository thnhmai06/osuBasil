using Basil.Application.Common;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks for a countdown in a room, which may start a round when it elapses.</summary>
/// <param name="Actor">The connection that acts.</param>
/// <param name="Room">The room it acts on.</param>
/// <param name="Duration">How long the countdown runs, from one second to one hour.</param>
/// <param name="StartsRound">Whether the round starts when the countdown ends.</param>
/// <remarks>
///     Starting a countdown needs the right to start rounds. The answer is Ok, a denial, InvalidSettings (the duration is
///     out of range), InProgress (a round is playing and the countdown would start one) or RoomClosed. A countdown
///     replaces the one already running in the room.
/// </remarks>
public sealed record StartRoomCountdown(Connection Actor, Room Room, TimeSpan Duration, bool StartsRound)
	: IRequest<Result<RoomOutcome>>;