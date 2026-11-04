using Basil.Application.Common;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks Multiplayer to start a countdown in a room.</summary>
/// <param name="Actor">The connection that acts.</param>
/// <param name="Room">The room it acts on.</param>
/// <param name="Length">How long the countdown runs.</param>
/// <param name="StartsRound">Whether the round starts when the countdown ends.</param>
/// <remarks>The answer is what the operation answers for the actor's permissions and the room's state.</remarks>
public sealed record StartCountdown(Connection Actor, Room Room, TimeSpan Length, bool StartsRound) : IRequest<Result<RoomOutcome>>;
