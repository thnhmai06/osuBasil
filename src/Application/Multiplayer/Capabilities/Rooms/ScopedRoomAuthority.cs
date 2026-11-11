using Basil.Application.Shared;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

/// <summary>Authority operations on a room, scoped to a connection with its permissions.</summary>
public readonly struct ScopedRoomAuthority
{
	private readonly ScopedRoom _scoped;

	/// <summary>Creates a scoped authority service for a room.</summary>
	/// <param name="scoped">The scoped room.</param>
	internal ScopedRoomAuthority(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Makes a user a referee.</summary>
	/// <param name="user">The user to make referee, named by id or name.</param>
	/// <returns>Ok, NotAuthorized, NotInRoom for a user who does not exist, IsCreator, AlreadyReferee, TooManyReferees or RoomClosed when the room has closed.</returns>
	public async Task<RoomOutcome> AddRefereeAsync(UserReference user)
	{
		if (_scoped.Require(RoomPrivileges.RefereesManage) is { } denied) return denied;
		return await _scoped.Room.Context.Users.FindAsync(user) is { } record
			? _scoped.Room.AddReferee(_scoped.Actor.Record, record)
			: RoomOutcome.NotInRoom;
	}

	/// <summary>Removes a user from the referees.</summary>
	/// <param name="user">The referee to remove, named by id or name.</param>
	/// <returns>Ok, NotAuthorized, NotReferee, NotInRoom for a user who does not exist, or RoomClosed when the room has closed.</returns>
	public async Task<RoomOutcome> RemoveRefereeAsync(UserReference user)
	{
		if (_scoped.Require(RoomPrivileges.RefereesManage) is { } denied) return denied;
		return await _scoped.Room.Context.Users.FindAsync(user) is { } record
			? _scoped.Room.RemoveReferee(_scoped.Actor.Record, record)
			: RoomOutcome.NotInRoom;
	}

	/// <summary>Gives host to a seated player, or clears it.</summary>
	/// <param name="host">The seated player to make host, or <see langword="null" /> to clear the host.</param>
	/// <returns>Ok, NotAuthorized, NotInRoom or RoomClosed when the room has closed.</returns>
	/// <remarks>Giving host to the current host does nothing.</remarks>
	public Task<RoomOutcome> SetHostAsync(StableConnection? host)
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.HostTransfer) ??
		                       _scoped.Room.SetHost(_scoped.Actor.Record, host));
	}
}