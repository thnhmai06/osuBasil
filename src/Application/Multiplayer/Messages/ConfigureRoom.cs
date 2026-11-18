using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record ConfigureRoom(Connection Actor, Room Room, RoomSettingsChange Change)
	: IRequest<RoomOutcome>;