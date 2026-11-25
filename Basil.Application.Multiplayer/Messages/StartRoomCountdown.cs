using Basil.Application.Sessions.Objects;
using Basil.Domain.Multiplayer.Room;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks a room to run a countdown, optionally starting the round when it ends.</summary>
/// <param name="Actor">The session asking.</param>
/// <param name="Room">The room to count down in.</param>
/// <param name="Duration">The length of the countdown; <see langword="null"/> means <see cref="DefaultDuration"/>.</param>
/// <param name="StartsRound">Whether the round starts when the countdown ends.</param>
public sealed record StartRoomCountdown(Session Actor, Room Room, TimeSpan? Duration, bool StartsRound)
	: IRequest<RoomActionResult>
{
	/// <summary>The length of a countdown that gives none.</summary>
	public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(30);

	/// <summary>Gets the length the countdown runs for.</summary>
	public TimeSpan Length => Duration ?? DefaultDuration;
}