using Basil.Application.Common;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks Multiplayer to seat the actor in a room.</summary>
/// <param name="Actor">The connection that acts.</param>
/// <param name="Room">The room it acts on.</param>
/// <param name="Password">The password the actor offers.</param>
/// <remarks>The answer is what the operation answers for the actor's permissions and the room's state.</remarks>
public sealed record JoinRoom(Connection Actor, Room Room, string Password) : IRequest<Result<RoomOutcome>>;
