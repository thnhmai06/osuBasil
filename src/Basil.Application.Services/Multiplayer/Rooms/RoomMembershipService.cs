using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Contracts.Multiplayer.Events;
using Basil.Application.Contracts.Multiplayer.Rooms;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Application.Services.Multiplayer.Rooms;

/// <summary>Seats, removes and invites the people of a room.</summary>
internal sealed class RoomMembershipService(
	Lobby lobby,
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
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (!player.IsOpen) return RoomResult.TargetOffline;
		if (player.User.Value.SilenceEndsAt > time.GetUtcNow()) return RoomResult.Silenced;
		if (!player.User.Value.Privilege.Has(ClientPrivileges.Player)) return RoomResult.NotAuthorized;
		if (room.Banned.Contains(player.User)) return RoomResult.Banned;

		if (lobby.RoomOf(player) is { } other && !ReferenceEquals(other, room))
		{
			if (!RoomRules.CanManage(other, by)) return RoomResult.InAnotherRoom;
			if (!room.Slots.Any(s => s is { Locked: false, Player: null })) return RoomResult.Full;
			if (room.Observers.Any(observer => observer.User.Equals(player.User))) return RoomResult.IsObserver;
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
				await using var scope = await Lobby.EnterAsync(room, cancellationToken);
				if (scope is not null) Leave(room, player);
			}
		}
		else if (connection is TourneyConnection observer)
		{
			foreach (var room in lobby.Rooms.Where(room => room.Observers.Contains(observer)).ToArray())
			{
				await using var scope = await Lobby.EnterAsync(room, cancellationToken);
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

		if (room.Banned.Contains(by.User)) return RoomResult.Banned;
		if (by.User.Value.SilenceEndsAt > time.GetUtcNow()) return RoomResult.Silenced;
		if (!by.User.Value.Privilege.Has(ClientPrivileges.Player)) return RoomResult.NotAuthorized;
		if (lobby.RoomOf(by) is { } other && !ReferenceEquals(other, room)) return RoomResult.InAnotherRoom;
		if (room.Observers.Any(observer => observer.User.Equals(by.User))) return RoomResult.IsObserver;
		if (!string.IsNullOrEmpty(room.Password) && room.Password != password &&
		    !by.User.Value.Privilege.Has(ClientPrivileges.Moderator))
			return RoomResult.WrongPassword;

		return TakeSeat(room, by, stale);
	}

	private RoomResult Seat(Room room, Connection by, BanchoConnection player)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (!player.IsOpen) return RoomResult.TargetOffline;

		var stale = room.Slots.Find(player.User)?.Player;
		if (stale is not null && (ReferenceEquals(stale, player) || stale.IsOpen)) return RoomResult.AlreadySeated;

		if (room.Banned.Contains(player.User)) return RoomResult.Banned;
		if (lobby.RoomOf(player) is { } other && !ReferenceEquals(other, room)) return RoomResult.InAnotherRoom;
		return room.Observers.Any(observer => observer.User.Equals(player.User))
			? RoomResult.IsObserver
			: TakeSeat(room, player, stale);
	}

	/// <summary>Seats a player who passed the room's checks, taking over the seat of a closed connection of the same user.</summary>
	private RoomResult TakeSeat(Room room, BanchoConnection player, BanchoConnection? stale)
	{
		if (stale is not null) return ReplaceSeat(room, player, stale);

		if (RoomSlotsMechanics.Seat(room, player) is not { } slot)
		{
			ReportIfEmpty(room);
			return RoomResult.Full;
		}

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
		var wasHost = ReferenceEquals(room.Host, stale);

		RoomSlotsMechanics.Clear(slot);
		RoomSlotsMechanics.Occupy(slot, player);
		slot.Team = team;
		if (wasHost) room.Host = player;

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
		events.Emit(new RoomPlayerLeft(room, by, slot.Index, room.Host, progress));
		roomChannel.LeaveChannel(room, by);
		ReportIfEmpty(room);
		return RoomResult.Ok;
	}

	private RoomResult Kick(Room room, Connection by, User player)
	{
		if (!RoomRules.IsHostOrManager(room, by)) return RoomResult.NotAuthorized;
		if (RoomRules.IsManager(room, player)) return RoomResult.IsManager;
		if (room.Slots.Find(player) is not { Player: { } seated }) return RoomResult.NotInRoom;

		var slot = RoomSlotsMechanics.Vacate(room, seated)!;
		var progress = rounds.AdvanceRound(room);
		events.Emit(new RoomPlayerKicked(room, seated, slot.Index, room.Host, progress));
		roomChannel.LeaveChannel(room, seated);
		ReportIfEmpty(room);
		return RoomResult.Ok;
	}

	private RoomResult Ban(Room room, Connection by, User player)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (player.Id == SystemUserIds.BasilBot) return RoomResult.NotAuthorized;
		if (RoomRules.IsManager(room, player)) return RoomResult.IsManager;
		if (!room.AddBanned(player)) return RoomResult.Ok;

		int? vacated = null;
		BanchoConnection? evicted = null;
		RoomRoundProgress? progress = null;
		if (room.Slots.Find(player) is { Player: { } seated })
		{
			evicted = seated;
			vacated = RoomSlotsMechanics.Vacate(room, seated)?.Index;
			progress = rounds.AdvanceRound(room);
		}

		events.Emit(new RoomPlayerBanned(room, player, vacated, evicted, room.Host, progress));
		if (evicted is not null)
		{
			roomChannel.LeaveChannel(room, evicted);
			ReportIfEmpty(room);
		}

		return RoomResult.Ok;
	}

	private RoomResult Unban(Room room, Connection by, User player)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (!room.RemoveBanned(player)) return RoomResult.NotBanned;

		events.Emit(new RoomPlayerUnbanned(room, player));
		return RoomResult.Ok;
	}

	private RoomResult Invite(Room room, Connection by, UserSession target)
	{
		var seated = by is BanchoConnection player && room.Slots.Find(player) is not null;
		if (!seated && !RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (target.Bot is not null || !target.Connections.Any(connection => connection.IsOpen))
			return RoomResult.TargetOffline;
		if (room.Slots.Find(target.User) is not null) return RoomResult.AlreadyInRoom;

		events.Emit(new RoomPlayerInvited(room, by.User, target.User));
		return RoomResult.Ok;
	}

	private RoomResult ObserverJoin(Room room, TourneyConnection by)
	{
		if (room.Slots.Find(by.User) is not null) return RoomResult.IsPlayer;
		if (!room.AddObserver(by)) return RoomResult.Ok;

		events.Emit(new RoomObserverJoined(room, by));
		roomChannel.JoinChannel(room, by);
		return RoomResult.Ok;
	}

	private RoomResult ObserverLeave(Room room, TourneyConnection by)
	{
		if (!room.RemoveObserver(by)) return RoomResult.NotObserver;

		events.Emit(new RoomObserverLeft(room, by));
		roomChannel.LeaveChannel(room, by);
		return RoomResult.Ok;
	}
}