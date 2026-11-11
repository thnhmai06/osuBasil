using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Shared;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks Multiplayer to move the actor to a slot of a room.</summary>
/// <param name="Actor">The connection that acts.</param>
/// <param name="Room">The room it acts on.</param>
/// <param name="Index">The slot to move to.</param>
/// <remarks>The answer is what the operation answers for the actor's privileges and the room's state.</remarks>
public sealed record ChangeSlot(Connection Actor, Room Room, int Index) : IRequest<RoomOutcome>;