using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Contracts.Multiplayer.Events;
using Basil.Application.Contracts.Multiplayer.Rooms;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Multiplayer.Rooms;

/// <summary>Changes the referees and the host of a room.</summary>
internal sealed class RoomAuthorityService(RoomEventStream events) : IRoomAuthorityService
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
		if (!RoomRules.IsCreatorOrBot(room, by)) return RoomResult.NotAuthorized;
		if (user.Id == SystemUserIds.BasilBot) return RoomResult.NotAuthorized;
		if (room.Creator is not null && room.Creator.Equals(user)) return RoomResult.IsCreator;
		if (room.Referees.Contains(user)) return RoomResult.AlreadyReferee;
		if (room.Referees.Count >= Room.MaxReferees) return RoomResult.TooManyReferees;

		room.AddReferee(user);
		events.Emit(new RoomRefereeAdded(room, user));
		return RoomResult.Ok;
	}

	private RoomResult RemoveReferee(Room room, Connection by, User user)
	{
		if (!RoomRules.IsCreatorOrBot(room, by)) return RoomResult.NotAuthorized;
		if (!room.RemoveReferee(user)) return RoomResult.NotReferee;

		events.Emit(new RoomRefereeRemoved(room, user));
		return RoomResult.Ok;
	}

	private RoomResult SetHost(Room room, Connection by, BanchoConnection? host)
	{
		if (!RoomRules.IsHostOrManager(room, by)) return RoomResult.NotAuthorized;
		if (host is not null && room.Slots.Find(host) is null) return RoomResult.NotInRoom;
		if (ReferenceEquals(room.Host, host)) return RoomResult.Ok;

		room.Host = host;
		events.Emit(new RoomHostChanged(room, host));
		return RoomResult.Ok;
	}
}
