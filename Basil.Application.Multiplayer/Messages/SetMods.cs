using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer.Room;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record SetMods(Session Actor, Room Room, GameMods Mods) : IRequest<RoomActionResult>;