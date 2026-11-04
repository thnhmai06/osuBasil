using Basil.Application.Common;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks Multiplayer to lock or unlock a slot of a room.</summary>
/// <param name="Actor">The connection that acts.</param>
/// <param name="Room">The room it acts on.</param>
/// <param name="Index">The slot to toggle.</param>
/// <remarks>The answer is what the operation answers for the actor's permissions and the room's state.</remarks>
public sealed record ToggleSlotLock(Connection Actor, Room Room, int Index) : IRequest<Result<RoomOutcome>>;
