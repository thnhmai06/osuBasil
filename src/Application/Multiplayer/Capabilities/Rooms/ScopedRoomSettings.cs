using Basil.Application.Multiplayer.Objects;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

/// <summary>Settings operations on a room, scoped to a connection with its permissions.</summary>
public readonly struct ScopedRoomSettings
{
	private readonly ScopedRoom _scoped;

	/// <summary>Creates a scoped settings service for a room.</summary>
	/// <param name="scoped">The scoped room.</param>
	internal ScopedRoomSettings(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Changes the room's settings in one step.</summary>
	/// <param name="change">The settings to change.</param>
	/// <returns>Ok, NotAuthorized, InProgress, InvalidSettings, InvalidMods or RoomClosed when the room has closed.</returns>
	/// <remarks>
	///     Nothing changes unless every field is valid. Selecting or clearing the beatmap sets ready players back to not
	///     ready.
	///     Turning freemod on moves the room's mods that are not speed-changing onto each player; turning it off gives the
	///     room the host's mods. Changing the team type reassigns teams. Changing the mode drops mods, the room's and the
	///     players', that the new mode does not allow. Under freemod, a seated caller's mods that are not speed-changing
	///     become
	///     that caller's own mods. A change with no field set does nothing.
	///     A change to the beatmap, mode, mods, freemod, team type or win condition cancels a countdown that would start the
	///     round.
	/// </remarks>
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

	/// <summary>Locks or unlocks the room, which stops players from changing slot or team.</summary>
	/// <param name="locked">The lock state to set.</param>
	/// <returns>
	///     Ok, NotAuthorized or RoomClosed when the room has closed; setting the current state again returns Ok and does
	///     nothing.
	/// </returns>
	public Task<RoomOutcome> SetLockedAsync(bool locked)
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.SlotsMovePlayers) ??
		                       _scoped.Room.SetLocked(locked));
	}
}