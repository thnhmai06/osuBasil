using Basil.Application.Common;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks Multiplayer to remove a player from a room.</summary>
/// <param name="Actor">The connection that acts.</param>
/// <param name="Room">The room it acts on.</param>
/// <param name="Player">The player to remove.</param>
/// <remarks>The answer is what the operation answers for the actor's permissions and the room's state.</remarks>
public sealed record KickPlayer(Connection Actor, Room Room, UserRecord Player) : IRequest<Result<RoomOutcome>>;
