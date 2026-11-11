using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Shared;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks Multiplayer to lock or unlock a room.</summary>
/// <param name="Actor">The connection that acts.</param>
/// <param name="Room">The room it acts on.</param>
/// <param name="Locked">Whether nobody else may join.</param>
/// <remarks>The answer is what the operation answers for the actor's privileges and the room's state.</remarks>
public sealed record SetRoomLocked(Connection Actor, Room Room, bool Locked) : IRequest<RoomOutcome>;