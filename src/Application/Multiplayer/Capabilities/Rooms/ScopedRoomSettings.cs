using Basil.Application.Common.Common;
using Basil.Application.Multiplayer.Objects;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

/// <summary>Settings operations on a room, scoped to a connection with its permissions.</summary>
public sealed class ScopedRoomSettings
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
	/// <param name="cancellationToken">A token that cancels the operation.</param>
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
	public Task<Result<RoomOutcome>> ConfigureAsync(RoomSettingsChange change,
		CancellationToken cancellationToken = default)
	{
		// Build required permissions from the change
		var required = RoomPermissions.SettingsChange;
		if (change.Name is not null) required |= RoomPermissions.SettingsRename;
		if (change.Password is not null) required |= RoomPermissions.SettingsPassword;
		if (change.IsPrivate is not null) required |= RoomPermissions.SettingsPrivate;

		return Task.FromResult(_scoped.Require(required) ??
		                       _scoped.Room.Configure(_scoped.By, change));
	}


	/// <summary>Locks or unlocks the room, which stops players from changing slot or team.</summary>
	/// <param name="locked">The lock state to set.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     Ok, NotAuthorized or RoomClosed when the room has closed; setting the current state again returns Ok and does
	///     nothing.
	/// </returns>
	public Task<Result<RoomOutcome>> SetLockedAsync(bool locked, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.SlotsMovePlayers) ??
		                       _scoped.Room.SetLocked(locked));
	}
}