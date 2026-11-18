using Basil.Application.Multiplayer.Objects;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

public readonly struct ScopedRoomSettings
{
	private readonly ScopedRoom _scoped;

	internal ScopedRoomSettings(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	public Task<RoomOutcome> ConfigureAsync(RoomSettingsChange change)
	{
		// Build required permissions from the change
		var required = RoomPrivileges.SettingsChange;
		if (change.Name is not null) required |= RoomPrivileges.SettingsRename;
		if (change.Password is not null) required |= RoomPrivileges.SettingsPassword;
		if (change.IsPrivate is not null) required |= RoomPrivileges.SettingsPrivate;

		return Task.FromResult(_scoped.Require(required) ??
		                       _scoped.Room.Configure(_scoped.Actor, change));
	}

	public Task<RoomOutcome> SetLockedAsync(bool locked)
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.SlotsMovePlayers) ??
		                       _scoped.Room.SetLocked(locked));
	}
}