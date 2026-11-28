using Basil.Application.Multiplayer.Objects;
using Basil.Application.Sessions.Objects;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks whether a tournament client may look at a room's state without joining it: <see cref="RoomJoinResult.Ok"/> when it may, <see cref="RoomJoinResult.NotObserver"/> when the session is not a tournament client, <see cref="RoomJoinResult.Denied"/> when the room is hidden from it.</summary>
/// <param name="Actor">The session asking.</param>
/// <param name="Room">The room.</param>
public sealed record InspectRoom(Session Actor, Room Room) : IRequest<RoomJoinResult>;
