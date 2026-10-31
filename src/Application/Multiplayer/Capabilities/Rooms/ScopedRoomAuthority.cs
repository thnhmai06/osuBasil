using Basil.Application.Common.Common;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

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
	public Task<Result<RoomOutcome>> AddRefereeAsync(UserRecord user,
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.RefereesManage) ??
		                       _scoped.Room.AddReferee(_scoped.By.Record, user));
	}

	/// <summary>Removes a user from the referees.</summary>
	/// <param name="user">The referee to remove.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotReferee or RoomClosed when the room has closed.</returns>
	public Task<Result<RoomOutcome>> RemoveRefereeAsync(UserRecord user,
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.RefereesManage) ??
		                       _scoped.Room.RemoveReferee(_scoped.By.Record, user));
	}

	/// <summary>Gives host to a seated player, or clears it.</summary>
	/// <param name="host">The seated player to make host, or <see langword="null" /> to clear the host.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotInRoom or RoomClosed when the room has closed.</returns>
	/// <remarks>Giving host to the current host does nothing.</remarks>
	public Task<Result<RoomOutcome>> SetHostAsync(BanchoConnection? host,
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.HostTransfer) ??
		                       _scoped.Room.SetHost(_scoped.By.Record, host));
	}
}