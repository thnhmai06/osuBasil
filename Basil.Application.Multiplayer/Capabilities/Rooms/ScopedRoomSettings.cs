using Basil.Domain.Multiplayer.Room;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

public readonly struct ScopedRoomSettings
{
	private readonly ScopedRoom _scoped;

	internal ScopedRoomSettings(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	public RoomActionResult? RequireConfigure(RoomSettingsChange change)
	{
		// Making the room private is for moderators; every other change is for the staff.
		return _scoped.Require(change.IsPrivate is not null ? RoomPrivileges.Moderators : RoomPrivileges.Staff);
	}

	public Task<RoomActionResult> ConfigureAsync(RoomSettingsChange change)
	{
		return Task.FromResult(RequireConfigure(change) ?? _scoped.Room.Configure(_scoped.Actor, change));
	}

	public Task<RoomActionResult> ChangeAsync(WantedRoomSettings wanted)
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.Staff) ??
		                       _scoped.Room.ChangeSettings(_scoped.Actor, wanted));
	}

	public Task<RoomActionResult> SetLockedAsync(bool locked)
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.Moderators) ??
		                       _scoped.Room.SetLocked(locked));
	}
}