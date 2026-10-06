using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Services.Implementations.Users;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Implementations.Multiplayer.Rooms;

/// <summary>Seats, removes and invites the people of a room.</summary>
internal sealed class RoomMembershipService(
	ILobby lobby,
	LobbyService lobbyService,
	RoomEventStream events,
	RoomChannelService roomChannel,
	RoomRoundsService rounds,
	TimeProvider time) : IRoomMembershipService
{
	/// <inheritdoc />
	public Task<RoomResult> JoinAsync(Room room, BanchoConnection by, string password,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => Join(room, by, password), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> LeaveAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => Leave(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public async Task<RoomResult> SeatAsync(Room room, Connection by, BanchoConnection player,
		CancellationToken cancellationToken = default)
	{
		if (!RoomRules.CanManage(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;
		if (!player.IsOpen) return RoomResult.TargetOffline;
		switch (PermissionRules.Check(player, Permissions.PlayerJoinRoom, time.GetUtcNow()))
		{
			case Access.NotGranted: return RoomResult.NotAuthorized;
			case Access.Suspended: return RoomResult.Silenced;
		}
		if (room.Members.Banned.Contains(player.User)) return RoomResult.Banned;

		if (lobby.RoomOf(player) is { } other && !ReferenceEquals(other, room))
		{
			if (!RoomRules.CanManage(other, by, time.GetUtcNow())) return RoomResult.InAnotherRoom;
			if (!room.Slots.Any(s => s is { Locked: false, Player: null })) return RoomResult.Full;
			if (room.Members.Observers.Any(observer => observer.User.Equals(player.User))) return RoomResult.IsObserver;
			await LeaveAsync(other, player, cancellationToken);
		}

		return await RoomScope.InScopeAsync(room, () => Seat(room, by, player), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> InviteAsync(Room room, Connection by, UserSession target,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => Invite(room, by, target), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> ObserverJoinAsync(Room room, TourneyConnection by,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => ObserverJoin(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> ObserverLeaveAsync(Room room, TourneyConnection by,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => ObserverLeave(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public async Task ReleaseAsync(Connection connection, CancellationToken cancellationToken = default)
	{
		if (connection is BanchoConnection player)
		{
			if (lobby.RoomOf(player) is { } room)
			{
				await using var scope = await room.EnterAsync(cancellationToken);
				if (scope is not null) Leave(room, player);
			}
		}
		else if (connection is TourneyConnection observer)
		{
			foreach (var room in lobby.Rooms.Where(room => room.Members.Observers.Contains(observer)).ToArray())
			{
				await using var scope = await room.EnterAsync(cancellationToken);
				if (scope is not null) ObserverLeave(room, observer);
			}
		}

		// Managers stay in a room's channel after leaving their seat, and IRC referees join it directly.
		roomChannel.LeaveAllChannels(connection);
	}

	/// <inheritdoc />
	public Task<RoomResult> KickAsync(Room room, Connection by, User player,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => Kick(room, by, player), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> BanAsync(Room room, Connection by, User player,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => Ban(room, by, player), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> UnbanAsync(Room room, Connection by, User player,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => Unban(room, by, player), cancellationToken);
	}

	/// <summary>Tells the lobby that a room has no players left, if that is so.</summary>
	/// <param name="room">The room to check.</param>
	internal void ReportIfEmpty(Room room)
	{
		if (!room.Slots.Any(slot => slot.Player is not null)) lobbyService.RoomEmptied(room);
	}

	private RoomResult Join(Room room, BanchoConnection by, string password)
	{
		var stale = room.Slots.Find(by.User)?.Player;
		if (stale is not null && (ReferenceEquals(stale, by) || stale.IsOpen)) return RoomResult.AlreadySeated;

		if (room.Members.Banned.Contains(by.User)) return RoomResult.Banned;
		switch (PermissionRules.Check(by, Permissions.PlayerJoinRoom, time.GetUtcNow()))
		{
			case Access.NotGranted: return RoomResult.NotAuthorized;
			case Access.Suspended: return RoomResult.Silenced;
		}
		if (lobby.RoomOf(by) is { } other && !ReferenceEquals(other, room)) return RoomResult.InAnotherRoom;
		if (room.Members.Observers.Any(observer => observer.User.Equals(by.User))) return RoomResult.IsObserver;
		if (!string.IsNullOrEmpty(room.Settings.Password) && room.Settings.Password != password &&
		    !PermissionRules.Allows(by, Permissions.TournamentManageAnyRoom, time.GetUtcNow()))
			return RoomResult.WrongPassword;

		return TakeSeat(room, by, stale);
	}

	private RoomResult Seat(Room room, Connection by, BanchoConnection player)
	{
		if (!RoomRules.CanManage(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;
		if (!player.IsOpen) return RoomResult.TargetOffline;

		var stale = room.Slots.Find(player.User)?.Player;
		if (stale is not null && (ReferenceEquals(stale, player) || stale.IsOpen)) return RoomResult.AlreadySeated;

		if (room.Members.Banned.Contains(player.User)) return RoomResult.Banned;
		if (lobby.RoomOf(player) is { } other && !ReferenceEquals(other, room)) return RoomResult.InAnotherRoom;
		return room.Members.Observers.Any(observer => observer.User.Equals(player.User))
			? RoomResult.IsObserver
			: TakeSeat(room, player, stale);
	}

	/// <summary>Seats a player who passed the room's checks, taking over the seat of a closed connection of the same user.</summary>
	private RoomResult TakeSeat(Room room, BanchoConnection player, BanchoConnection? stale)
	{
		if (stale is not null) return ReplaceSeat(room, player, stale);

		if (RoomSlotsMechanics.Seat(room, player) is not { } slot) return RoomResult.Full;

		roomChannel.JoinChannel(room, player);
		LobbyService.RoomOccupied(room);
		events.Emit(new RoomPlayerJoined(room, player, slot.Index, null, null));
		return RoomResult.Ok;
	}

	/// <summary>Gives the seat of a closed connection to a new connection of the same user.</summary>
	private RoomResult ReplaceSeat(Room room, BanchoConnection player, BanchoConnection stale)
	{
		var slot = room.Slots.Find(stale)!;
		var team = slot.Team;
		var wasHost = ReferenceEquals(room.Authority.Host, stale);

		RoomSlotsMechanics.Clear(slot);
		RoomSlotsMechanics.Occupy(slot, player);
		slot.Team = team;
		if (wasHost) room.Authority.Host = player;

		roomChannel.LeaveChannel(room, stale);
		var progress = rounds.AdvanceRound(room);
		roomChannel.JoinChannel(room, player);
		LobbyService.RoomOccupied(room);
		events.Emit(new RoomPlayerJoined(room, player, slot.Index, stale, progress));
		return RoomResult.Ok;
	}

	private RoomResult Leave(Room room, BanchoConnection by)
	{
		if (RoomSlotsMechanics.Vacate(room, by) is not { } slot) return RoomResult.NotInRoom;

		var progress = rounds.AdvanceRound(room);
		events.Emit(new RoomPlayerLeft(room, by, slot.Index, room.Authority.Host, progress));
		roomChannel.LeaveChannel(room, by);
		ReportIfEmpty(room);
		return RoomResult.Ok;
	}

	private RoomResult Kick(Room room, Connection by, User player)
	{
		if (!RoomRules.IsHostOrManager(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;
		if (RoomRules.IsManager(room, player)) return RoomResult.IsManager;
		if (room.Slots.Find(player) is not { Player: { } seated }) return RoomResult.NotInRoom;

		var slot = RoomSlotsMechanics.Vacate(room, seated)!;
		var progress = rounds.AdvanceRound(room);
		events.Emit(new RoomPlayerKicked(room, by.User, seated, slot.Index, room.Authority.Host, progress));
		roomChannel.LeaveChannel(room, seated);
		ReportIfEmpty(room);
		return RoomResult.Ok;
	}

	private RoomResult Ban(Room room, Connection by, User player)
	{
		if (!RoomRules.CanManage(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;
		if (RoomRules.IsManager(room, player)) return RoomResult.IsManager;
		if (!room.Members.AddBanned(player)) return RoomResult.Ok;

		int? vacated = null;
		BanchoConnection? evicted = null;
		RoomRoundProgress? progress = null;
		if (room.Slots.Find(player) is { Player: { } seated })
		{
			evicted = seated;
			vacated = RoomSlotsMechanics.Vacate(room, seated)?.Index;
			progress = rounds.AdvanceRound(room);
		}

		events.Emit(new RoomPlayerBanned(room, by.User, player, vacated, evicted, room.Authority.Host, progress));
		if (evicted is not null)
		{
			roomChannel.LeaveChannel(room, evicted);
			ReportIfEmpty(room);
		}

		return RoomResult.Ok;
	}

	private RoomResult Unban(Room room, Connection by, User player)
	{
		if (!RoomRules.CanManage(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;
		if (!room.Members.RemoveBanned(player)) return RoomResult.NotBanned;

		events.Emit(new RoomPlayerUnbanned(room, player));
		return RoomResult.Ok;
	}

	private RoomResult Invite(Room room, Connection by, UserSession target)
	{
		var seated = by is BanchoConnection player && room.Slots.Find(player) is not null;
		if (!seated && !RoomRules.CanManage(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;
		if (!target.Connections.Any(connection => connection.IsOpen)) return RoomResult.TargetOffline;
		if (room.Slots.Find(target.User) is not null) return RoomResult.AlreadyInRoom;

		events.Emit(new RoomPlayerInvited(room, by.User, target.User));
		return RoomResult.Ok;
	}

	private RoomResult ObserverJoin(Room room, TourneyConnection by)
	{
		if (room.Slots.Find(by.User) is not null) return RoomResult.IsPlayer;
		if (!room.Members.AddObserver(by)) return RoomResult.Ok;

		events.Emit(new RoomObserverJoined(room, by));
		roomChannel.JoinChannel(room, by);
		return RoomResult.Ok;
	}

	private RoomResult ObserverLeave(Room room, TourneyConnection by)
	{
		if (!room.Members.RemoveObserver(by)) return RoomResult.NotObserver;

		events.Emit(new RoomObserverLeft(room, by));
		roomChannel.LeaveChannel(room, by);
		return RoomResult.Ok;
	}
}