using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer.Room;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record SetRoomLocked(Session Actor, Room Room, bool Locked) : IRequest<RoomOutcome>;