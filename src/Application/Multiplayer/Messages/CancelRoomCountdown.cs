using Basil.Application.Common;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks to cancel the countdown running in a room.</summary>
/// <param name="Actor">The connection that acts.</param>
/// <param name="Room">The room it acts on.</param>
/// <remarks>
///     Cancelling needs the privilege to start rounds. The answer is Ok, a denial, NotInProgress (no countdown is running) or
///     RoomClosed.
/// </remarks>
public sealed record CancelRoomCountdown(Connection Actor, Room Room) : IRequest<Result<RoomOutcome>>;