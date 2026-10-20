using Basil.Application.Objects.Multiplayer.Events;
using Basil.Application.Objects.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Objects.Multiplayer.Rooms;

/// <summary>Authority operations on a room, scoped to a connection with its permissions.</summary>
public sealed class ScopedRoomAuthority
{
	private readonly ScopedRoom _scoped;

	/// <summary>Creates a scoped authority service for a room.</summary>
	/// <param name="scoped">The scoped room.</param>
	internal ScopedRoomAuthority(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Makes a user a referee.</summary>
	/// <param name="user">The user to make referee.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, IsCreator, AlreadyReferee, TooManyReferees or RoomClosed when the room has closed.</returns>
	public async Task<RoomResult> AddRefereeAsync(UserRecord user, CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.RefereesManage) is { } denied) return denied;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var room = _scoped.Room;
		if (room.Authority.Creator is not null && room.Authority.Creator.Equals(user)) return RoomResult.IsCreator;
		if (room.Authority.Referees.Contains(user)) return RoomResult.AlreadyReferee;
		if (room.Authority.Referees.Count >= RoomAuthority.MaxReferees) return RoomResult.TooManyReferees;

		room.Authority.MutableReferees.Add(user);
		room.Context.Events.Publish(new RoomRefereeAdded(room, _scoped.By.Record, user));
		return RoomResult.Ok;
	}

	/// <summary>Removes a user from the referees.</summary>
	/// <param name="user">The referee to remove.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotReferee or RoomClosed when the room has closed.</returns>
	public async Task<RoomResult> RemoveRefereeAsync(UserRecord user, CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.RefereesManage) is { } denied) return denied;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var room = _scoped.Room;
		if (!room.Authority.MutableReferees.Remove(user)) return RoomResult.NotReferee;

		room.Context.Events.Publish(new RoomRefereeRemoved(room, _scoped.By.Record, user));
		return RoomResult.Ok;
	}

	/// <summary>Gives host to a seated player, or clears it.</summary>
	/// <param name="host">The seated player to make host, or <see langword="null" /> to clear the host.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotInRoom or RoomClosed when the room has closed.</returns>
	/// <remarks>Giving host to the current host does nothing.</remarks>
	public async Task<RoomResult> SetHostAsync(BanchoConnection? host, CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.HostTransfer) is { } denied) return denied;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var room = _scoped.Room;
		if (host is not null && room.Slots.Find(host) is null) return RoomResult.NotInRoom;
		if (ReferenceEquals(room.Authority.Host, host)) return RoomResult.Ok;

		room.Authority.Host = host;
		room.Context.Events.Publish(new RoomHostChanged(room, _scoped.By.Record, host));
		return RoomResult.Ok;
	}
}