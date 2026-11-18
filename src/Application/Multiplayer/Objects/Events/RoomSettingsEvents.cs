using Basil.Application.Multiplayer.Objects.Rooms;

namespace Basil.Application.Multiplayer.Objects.Events;

public abstract record RoomSettingsEvent(Room Room) : RoomEvent(Room);

public sealed record RoomSettingsChanged(
	Room Room,
	RoomSettingsChange Change,
	bool PasswordChanged)
	: RoomSettingsEvent(Room);