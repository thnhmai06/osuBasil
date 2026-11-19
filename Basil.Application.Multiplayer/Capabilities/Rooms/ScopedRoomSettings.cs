using Basil.Domain.Multiplayer.Room;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

public readonly struct ScopedRoomSettings
{
	private readonly ScopedRoom _scoped;

	internal ScopedRoomSettings(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	public Task<RoomActionResult> ConfigureAsync(RoomSettingsChange change)
	{
		// Making the room private is for moderators; every other change is for the staff.
		var allowed = change.IsPrivate is not null ? RoomPrivileges.Moderators : RoomPrivileges.Staff;

		return Task.FromResult(_scoped.Require(allowed) ??
		                       _scoped.Room.Configure(_scoped.Actor, change));
	}

	public Task<RoomActionResult> SetLockedAsync(bool locked)
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.Moderators) ??
		                       _scoped.Room.SetLocked(locked));
	}
}