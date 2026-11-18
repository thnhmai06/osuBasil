using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Mechanics;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record SetMods(Connection Actor, Room Room, GameMods Mods) : IRequest<RoomOutcome>;