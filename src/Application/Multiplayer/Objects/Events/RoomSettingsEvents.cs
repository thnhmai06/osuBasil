using Basil.Domain.Multiplayer.Room;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Objects.Events;

public abstract record RoomSettingsEvent(Room Room) : RoomEvent(Room);

public sealed record RoomSettingsChanged(
	Room Room,
	RoomSettingsChange Change,
	bool PasswordChanged)
	: RoomSettingsEvent(Room);