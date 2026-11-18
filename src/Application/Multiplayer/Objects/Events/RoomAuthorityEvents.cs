using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Events;

public abstract record RoomAuthorityEvent(Room Room) : RoomEvent(Room);

public sealed record RoomHostChanged(Room Room, UserRecord Actor, StableConnection? Host) : RoomAuthorityEvent(Room);

public sealed record RoomRefereeAdded(Room Room, UserRecord Actor, UserRecord Referee) : RoomAuthorityEvent(Room);

public sealed record RoomRefereeRemoved(Room Room, UserRecord Actor, UserRecord Referee) : RoomAuthorityEvent(Room);