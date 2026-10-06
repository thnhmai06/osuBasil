using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Contracts.Multiplayer.Events;
using Basil.Application.Contracts.Multiplayer.Rooms;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Multiplayer.Rooms;

/// <summary>Changes the referees and the host of a room.</summary>
internal sealed class RoomAuthorityService(RoomEventStream events, TimeProvider time) : IRoomAuthorityService
{
	/// <inheritdoc />
	public Task<RoomResult> AddRefereeAsync(Room room, Connection by, User user,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => AddReferee(room, by, user), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> RemoveRefereeAsync(Room room, Connection by, User user,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => RemoveReferee(room, by, user), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SetHostAsync(Room room, Connection by, BanchoConnection? host,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => SetHost(room, by, host), cancellationToken);
	}

	private RoomResult AddReferee(Room room, Connection by, User user)
	{
		if (!RoomRules.IsCreatorOrAnyRoomManager(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;
		if (room.Authority.Creator is not null && room.Authority.Creator.Equals(user)) return RoomResult.IsCreator;
		if (room.Authority.Referees.Contains(user)) return RoomResult.AlreadyReferee;
		if (room.Authority.Referees.Count >= RoomAuthority.MaxReferees) return RoomResult.TooManyReferees;

		room.Authority.AddReferee(user);
		events.Emit(new RoomRefereeAdded(room, user));
		return RoomResult.Ok;
	}

	private RoomResult RemoveReferee(Room room, Connection by, User user)
	{
		if (!RoomRules.IsCreatorOrAnyRoomManager(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;
		if (!room.Authority.RemoveReferee(user)) return RoomResult.NotReferee;

		events.Emit(new RoomRefereeRemoved(room, user));
		return RoomResult.Ok;
	}

	private RoomResult SetHost(Room room, Connection by, BanchoConnection? host)
	{
		if (!RoomRules.IsHostOrManager(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;
		if (host is not null && room.Slots.Find(host) is null) return RoomResult.NotInRoom;
		if (ReferenceEquals(room.Authority.Host, host)) return RoomResult.Ok;

		room.Authority.Host = host;
		events.Emit(new RoomHostChanged(room, host));
		return RoomResult.Ok;
	}
}