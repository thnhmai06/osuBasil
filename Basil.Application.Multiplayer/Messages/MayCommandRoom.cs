using Basil.Application.Multiplayer.Objects;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Multiplayer.Room;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks whether a session holds one of the given roles in a room, so a refused command's body is not read at all.</summary>
public sealed record MayCommandRoom(Session Actor, Room Room, RoomPrivileges Roles) : IRequest<RoomActionResult>;
