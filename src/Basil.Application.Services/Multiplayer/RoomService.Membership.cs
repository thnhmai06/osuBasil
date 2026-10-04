using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Contracts.Multiplayer.Events;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Application.Services.Multiplayer;

internal sealed partial class RoomService
{
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
		if (room.Observers.Any(observer => observer.User.Equals(player.User))) return RoomResult.IsObserver;

		return TakeSeat(room, player, stale);
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

		channels.Join(room.Channel, player);
		lobbyService.RoomOccupied(room);
		Emit(new RoomPlayerJoined(room, player, slot.Index, null, null));
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

		LeaveChannel(room, stale);
		var progress = AdvanceRound(room);
		channels.Join(room.Channel, player);
		lobbyService.RoomOccupied(room);
		Emit(new RoomPlayerJoined(room, player, slot.Index, stale, progress));
		return RoomResult.Ok;
	}

	private RoomResult Leave(Room room, BanchoConnection by)
	{
		if (RoomSlotsMechanics.Vacate(room, by) is not { } slot) return RoomResult.NotInRoom;

		var progress = AdvanceRound(room);
		Emit(new RoomPlayerLeft(room, by, slot.Index, room.Host, progress));
		LeaveChannel(room, by);
		ReportIfEmpty(room);
		return RoomResult.Ok;
	}

	private RoomResult Kick(Room room, Connection by, User player)
	{
		if (!RoomRules.IsHostOrManager(room, by)) return RoomResult.NotAuthorized;
		if (RoomRules.IsManager(room, player)) return RoomResult.IsManager;
		if (room.Slots.Find(player) is not { Player: { } seated }) return RoomResult.NotInRoom;

		var slot = RoomSlotsMechanics.Vacate(room, seated)!;
		var progress = AdvanceRound(room);
		Emit(new RoomPlayerKicked(room, seated, slot.Index, room.Host, progress));
		LeaveChannel(room, seated);
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
			progress = AdvanceRound(room);
		}

		Emit(new RoomPlayerBanned(room, player, vacated, evicted, room.Host, progress));
		if (evicted is not null)
		{
			LeaveChannel(room, evicted);
			ReportIfEmpty(room);
		}

		return RoomResult.Ok;
	}

	private RoomResult Unban(Room room, Connection by, User player)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (!room.RemoveBanned(player)) return RoomResult.NotBanned;

		Emit(new RoomPlayerUnbanned(room, player));
		return RoomResult.Ok;
	}

	private RoomResult Invite(Room room, Connection by, UserSession target)
	{
		var seated = by is BanchoConnection player && room.Slots.Find(player) is not null;
		if (!seated && !RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (target.Bot is not null || !target.Connections.Any(connection => connection.IsOpen))
			return RoomResult.TargetOffline;
		if (room.Slots.Find(target.User) is not null) return RoomResult.AlreadyInRoom;

		Emit(new RoomPlayerInvited(room, by.User, target.User));
		return RoomResult.Ok;
	}

	private RoomResult AddReferee(Room room, Connection by, User user)
	{
		if (!RoomRules.IsCreatorOrBot(room, by)) return RoomResult.NotAuthorized;
		if (user.Id == SystemUserIds.BasilBot) return RoomResult.NotAuthorized;
		if (room.Creator is not null && room.Creator.Equals(user)) return RoomResult.IsCreator;
		if (room.Referees.Contains(user)) return RoomResult.AlreadyReferee;
		if (room.Referees.Count >= Room.MaxReferees) return RoomResult.TooManyReferees;

		room.AddReferee(user);
		Emit(new RoomRefereeAdded(room, user));
		return RoomResult.Ok;
	}

	private RoomResult RemoveReferee(Room room, Connection by, User user)
	{
		if (!RoomRules.IsCreatorOrBot(room, by)) return RoomResult.NotAuthorized;
		if (!room.RemoveReferee(user)) return RoomResult.NotReferee;

		Emit(new RoomRefereeRemoved(room, user));
		return RoomResult.Ok;
	}

	private RoomResult SetHost(Room room, Connection by, BanchoConnection? host)
	{
		if (!RoomRules.IsHostOrManager(room, by)) return RoomResult.NotAuthorized;
		if (host is not null && room.Slots.Find(host) is null) return RoomResult.NotInRoom;
		if (ReferenceEquals(room.Host, host)) return RoomResult.Ok;

		room.Host = host;
		Emit(new RoomHostChanged(room, host));
		return RoomResult.Ok;
	}

	private RoomResult ObserverJoin(Room room, TourneyConnection by)
	{
		if (room.Slots.Find(by.User) is not null) return RoomResult.IsPlayer;
		if (!room.AddObserver(by)) return RoomResult.Ok;

		Emit(new RoomObserverJoined(room, by));
		channels.Join(room.Channel, by);
		return RoomResult.Ok;
	}

	private RoomResult ObserverLeave(Room room, TourneyConnection by)
	{
		if (!room.RemoveObserver(by)) return RoomResult.NotObserver;

		Emit(new RoomObserverLeft(room, by));
		LeaveChannel(room, by);
		return RoomResult.Ok;
	}

	private void LeaveChannel(Room room, Connection connection)
	{
		if (!RoomRules.IsManager(room, connection.User)) channels.Part(room.Channel, connection);
	}

	private void ReportIfEmpty(Room room)
	{
		if (!room.Slots.Any(slot => slot.Player is not null)) lobbyService.RoomEmptied(room);
	}
}