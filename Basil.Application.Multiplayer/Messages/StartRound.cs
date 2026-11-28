using Basil.Application.Sessions.Objects;
using Basil.Domain.Multiplayer.Room;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks a room to start its round, at once or after a countdown.</summary>
/// <param name="Actor">The session asking.</param>
/// <param name="Room">The room to start.</param>
/// <param name="Countdown">The length of the countdown before the round starts; <see langword="null"/> or not above zero starts the round at once.</param>
public sealed record StartRound(Session Actor, Room Room, TimeSpan? Countdown = null) : IRequest<RoomActionResult>;