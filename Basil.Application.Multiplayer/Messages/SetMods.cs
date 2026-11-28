using Basil.Application.Sessions.Objects;
using Basil.Domain.Game;
using Basil.Domain.Multiplayer.Room;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record SetMods(Session Actor, Room Room, GameMods Mods) : IRequest<RoomActionResult>;