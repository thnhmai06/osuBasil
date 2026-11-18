using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record ConfigureRoom(Session Actor, Room Room, RoomSettingsChange Change)
	: IRequest<RoomOutcome>;