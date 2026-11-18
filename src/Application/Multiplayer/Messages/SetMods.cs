using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Game.Mechanics;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record SetMods(Session Actor, Room Room, GameMods Mods) : IRequest<RoomOutcome>;