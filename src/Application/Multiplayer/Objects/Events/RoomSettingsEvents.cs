using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Domain.Multiplayer;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Objects.Events;

public abstract record RoomSettingsEvent(Room Room) : RoomEvent(Room);

public sealed record RoomSettingsChanged(
	Room Room,
	RoomSettingsChange Change,
	bool PasswordChanged)
	: RoomSettingsEvent(Room);