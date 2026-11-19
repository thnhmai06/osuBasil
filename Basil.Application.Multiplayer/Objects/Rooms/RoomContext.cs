using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Shared.Events;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

internal sealed record RoomContext(
	TimeProvider Clock,
	IEventPublisher<RoomEvent> Events,
	IRoomLobbyPort Lobby,
	RoomUserLookup Users,
	ISettingsRepository Settings);

internal interface IRoomLobbyPort
{
	Room? RoomOf(StableSession player);

	void Occupied(Room room);

	void Emptied(Room room);

	void Close(Room room, UserRecord? actor);

	bool TryClaim(StableSession player, Room room);

	void Release(StableSession player, Room room);
}