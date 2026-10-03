using System.Threading.Channels;
using Basil.Application.Contracts.Chat;
using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Multiplayer;
using Basil.Application.Multiplayer.Events;
using Basil.Application.Sessions;
using Basil.Domain.Client;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Services.Multiplayer;

/// <summary>Changes rooms: membership, authority, settings, slots, rounds and countdowns.</summary>
internal sealed class RoomService(
	Lobby lobby,
	LobbyService lobbyService,
	IChannelService channels,
	TimeProvider time) : IRoomService
{
	/// <summary>The remaining times at which a running countdown is announced.</summary>
	internal static readonly TimeSpan[] CountdownMarks =
		[TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)];

	/// <summary>The countdown length used when none is given.</summary>
	internal static readonly TimeSpan DefaultCountdownLength = TimeSpan.FromSeconds(30);

	/// <summary>The longest countdown allowed.</summary>
	internal static readonly TimeSpan MaxCountdownLength = TimeSpan.FromHours(1);

	private readonly Channel<RoomEvent> _events = Channel.CreateUnbounded<RoomEvent>();

	/// <inheritdoc />
	public ChannelReader<RoomEvent> Events => _events.Reader;

	/// <inheritdoc />
	public Task<RoomResult> JoinAsync(Room room, BanchoConnection by, string password,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Join(room, by, password), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> LeaveAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Leave(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> KickAsync(Room room, Connection by, User player,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Kick(room, by, player), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> BanAsync(Room room, Connection by, User player,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Ban(room, by, player), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> UnbanAsync(Room room, Connection by, User player,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Unban(room, by, player), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> InviteAsync(Room room, Connection by, UserSession target,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Invite(room, by, target), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> AddRefereeAsync(Room room, Connection by, User user,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => AddReferee(room, by, user), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> RemoveRefereeAsync(Room room, Connection by, User user,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => RemoveReferee(room, by, user), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SetHostAsync(Room room, Connection by, BanchoConnection? host,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => SetHost(room, by, host), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> ObserverJoinAsync(Room room, TourneyConnection by,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => ObserverJoin(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> ObserverLeaveAsync(Room room, TourneyConnection by,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => ObserverLeave(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> ConfigureAsync(Room room, Connection by, RoomSettingsChange change,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Configure(room, by, change), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> ChangeSlotAsync(Room room, BanchoConnection by, int index,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => ChangeSlot(room, by, index), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> MoveAsync(Room room, Connection by, User player, int index,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Move(room, by, player, index), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> ToggleSlotLockAsync(Room room, Connection by, int index,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => ToggleSlotLock(room, by, index), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SetLockedAsync(Room room, Connection by, bool locked,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => SetLocked(room, by, locked), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SetReadyAsync(Room room, BanchoConnection by, bool ready,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => SetReady(room, by, ready), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SetHasMapAsync(Room room, BanchoConnection by, bool has,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => SetHasMap(room, by, has), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> ToggleTeamAsync(Room room, BanchoConnection by,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => ToggleTeam(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SetTeamAsync(Room room, Connection by, User player, GameTeam team,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => SetTeam(room, by, player, team), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SetPlayerModsAsync(Room room, BanchoConnection by, GameMods mods,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => SetPlayerMods(room, by, mods), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> StartAsync(Room room, Connection by, CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Start(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> AbortAsync(Room room, Connection by, CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Abort(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> MarkLoadedAsync(Room room, BanchoConnection by,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => MarkLoaded(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SkipAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Skip(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> FailAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Fail(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> CompleteAsync(Room room, BanchoConnection by,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => Complete(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> StartCountdownAsync(Room room, Connection by, TimeSpan length, bool startsRound,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => StartCountdown(room, by, length, startsRound), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> CancelCountdownAsync(Room room, Connection by,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => CancelCountdown(room, by), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> RecordScoreAsync(Room room, User player, Score score,
		CancellationToken cancellationToken = default)
	{
		return InScopeAsync(room, () => RecordScore(room, player, score), cancellationToken);
	}

	/// <inheritdoc />
	public async Task ReleaseAsync(Connection connection, CancellationToken cancellationToken = default)
	{
		if (connection is BanchoConnection player)
		{
			lobbyService.Unwatch(player);

			if (lobby.RoomOf(player) is { } room)
			{
				await using var scope = await lobby.EnterAsync(room, cancellationToken);
				if (scope is not null) Leave(room, player);
			}
		}
		else if (connection is TourneyConnection observer)
		{
			foreach (var room in lobby.Rooms.Where(room => room.Observers.Contains(observer)).ToArray())
			{
				await using var scope = await lobby.EnterAsync(room, cancellationToken);
				if (scope is not null) ObserverLeave(room, observer);
			}
		}

		// Managers stay in a room's channel after leaving their seat, and IRC referees join it directly.
		foreach (var room in lobby.Rooms)
			channels.Part(room.Channel, connection);
	}

	/// <inheritdoc />
	public bool IsManager(Room room, User user)
	{
		return RoomRules.IsManager(room, user);
	}

	/// <summary>Runs one state transition of a room inside the room's exclusive scope.</summary>
	/// <param name="room">The room to change.</param>
	/// <param name="operation">The transition to run once the scope is held.</param>
	/// <param name="cancellationToken">A token that cancels the wait for the scope.</param>
	/// <returns>The result of <paramref name="operation" />, or RoomClosed when the room has closed.</returns>
	private async Task<RoomResult> InScopeAsync(Room room, Func<RoomResult> operation,
		CancellationToken cancellationToken)
	{
		await using var scope = await lobby.EnterAsync(room, cancellationToken);
		return scope is null ? RoomResult.RoomClosed : operation();
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

		if (stale is not null)
		{
			// The closed connection leaves as an ordinary leave; the room is not reported empty
			// because the same user takes a seat right after.
			var vacated = RoomSlotsMechanics.Vacate(room, stale)!;
			Emit(new RoomPlayerLeft(room, stale, vacated.Index, room.Host));
			LeaveChannel(room, stale);
			AnnounceRoundProgress(room);
		}

		if (RoomSlotsMechanics.Seat(room, by) is not { } slot)
		{
			ReportIfEmpty(room);
			return RoomResult.Full;
		}

		Emit(new RoomPlayerJoined(room, by, slot.Index));
		channels.Join(room.Channel, by);
		lobbyService.RoomOccupied(room);
		return RoomResult.Ok;
	}

	private RoomResult Leave(Room room, BanchoConnection by)
	{
		if (RoomSlotsMechanics.Vacate(room, by) is not { } slot) return RoomResult.NotInRoom;

		Emit(new RoomPlayerLeft(room, by, slot.Index, room.Host));
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
		Emit(new RoomPlayerKicked(room, seated, slot.Index, room.Host));
		LeaveChannel(room, seated);
		ReportIfEmpty(room);
		return RoomResult.Ok;
	}

	private RoomResult Ban(Room room, Connection by, User player)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (RoomRules.IsManager(room, player)) return RoomResult.IsManager;
		if (!room.AddBanned(player)) return RoomResult.Ok;

		int? vacated = null;
		BanchoConnection? evicted = null;
		if (room.Slots.Find(player) is { Player: { } seated })
		{
			evicted = seated;
			vacated = RoomSlotsMechanics.Vacate(room, seated)?.Index;
		}

		Emit(new RoomPlayerBanned(room, player, vacated, evicted, room.Host));
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
		if (by is not BotConnection && (room.Creator is null || !room.Creator.Equals(by.User)))
			return RoomResult.NotAuthorized;
		if (room.Creator is not null && room.Creator.Equals(user)) return RoomResult.IsCreator;
		if (room.Referees.Contains(user)) return RoomResult.AlreadyReferee;
		if (room.Referees.Count >= Room.MaxReferees) return RoomResult.TooManyReferees;

		room.AddReferee(user);
		Emit(new RoomRefereeAdded(room, user));
		return RoomResult.Ok;
	}

	private RoomResult RemoveReferee(Room room, Connection by, User user)
	{
		if (by is not BotConnection && (room.Creator is null || !room.Creator.Equals(by.User)))
			return RoomResult.NotAuthorized;
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

	private RoomResult Configure(Room room, Connection by, RoomSettingsChange change)
	{
		if (!RoomRules.IsHostOrManager(room, by)) return RoomResult.NotAuthorized;
		if (room.InProgress) return RoomResult.InProgress;

		if (change.Name is not null && string.IsNullOrWhiteSpace(change.Name)) return RoomResult.InvalidSettings;
		if (change.Size is < 1 or > RoomSlots.MaxSlotCount) return RoomResult.InvalidSettings;
		if (change.ClearBeatmap && change.Beatmap is not null) return RoomResult.InvalidSettings;
		if (change.Mode is { } requestedMode && !Enum.IsDefined(requestedMode)) return RoomResult.InvalidSettings;
		if (change.TeamType is { } requestedTeamType && !Enum.IsDefined(requestedTeamType))
			return RoomResult.InvalidSettings;
		if (change.WinCondition is { } requestedWinCondition && !Enum.IsDefined(requestedWinCondition))
			return RoomResult.InvalidSettings;

		if (change == new RoomSettingsChange()) return RoomResult.Ok;

		var mode = change.Mode ?? room.Mode;
		var freemods = change.Freemods ?? room.Freemods;
		if (change.Mods is { } requestedMods && !requestedMods.IsValid(mode)) return RoomResult.InvalidMods;

		if (change.Name is { } name)
		{
			room.Match.Value.Name = name;
			room.Channel.Channel.Topic = name;
		}

		if (change.Password is { } password) room.Password = password;
		if (change.Beatmap is not null || change.ClearBeatmap)
		{
			room.Beatmap = change.Beatmap;
			foreach (var slot in room.Slots.Where(s => s.Status is RoomSlotStatus.Ready))
				RoomSlotsMechanics.SetStatus(slot, RoomSlotStatus.NotReady);
		}

		if (change.Mode is { } newMode)
		{
			room.Settings.SwitchMode(newMode);
			foreach (var slot in room.Slots.Where(s => s.Mods is not null))
				slot.Mods = slot.Mods!.Value.RemoveInvalidMods(newMode);
		}

		if (change.Freemods is { } newFreemods && newFreemods != room.Freemods) ApplyFreemods(room, newFreemods);
		if (change.Mods is { } newMods)
		{
			room.Settings.Mods = freemods ? newMods & GameMods.SpeedChangingMods : newMods;
			// Under freemod a seated caller keeps the rest of the mods as their own, as the osu! client expects.
			if (freemods && by is BanchoConnection caller && room.Slots.Find(caller) is { } callerSlot)
				callerSlot.Mods = newMods & ~GameMods.SpeedChangingMods;
		}

		if (change.TeamType is { } teamType) ApplyTeamType(room, teamType);
		if (change.WinCondition is { } winCondition) room.Settings.WinCondition = winCondition;
		if (change.Size is { } size) RoomSlotsMechanics.Resize(room.Slots, size);

		Emit(new RoomSettingsChanged(room, change with { Password = null }, change.Password is not null));
		return RoomResult.Ok;
	}

	private RoomResult ChangeSlot(Room room, BanchoConnection by, int index)
	{
		if (room.Slots.Find(by) is not { } from) return RoomResult.NotInRoom;
		if (room.Slots.Locked) return RoomResult.RoomLocked;
		if (room.InProgress) return RoomResult.InProgress;
		if (room.Slots.At(index) is not { } to) return RoomResult.SlotNotOpen;
		if (ReferenceEquals(from, to)) return RoomResult.Ok;
		if (to.Locked || to.Player is not null) return RoomResult.SlotNotOpen;

		RoomSlotsMechanics.MoveTo(from, to);
		Emit(new RoomPlayerMoved(room, by, from.Index, to.Index));
		return RoomResult.Ok;
	}

	private RoomResult Move(Room room, Connection by, User player, int index)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (room.Slots.Find(player) is not { Player: { } seated } from) return RoomResult.NotInRoom;
		if (room.Slots.At(index) is not { } to) return RoomResult.SlotNotOpen;
		if (ReferenceEquals(from, to)) return RoomResult.Ok;
		if (to.Locked || to.Player is not null) return RoomResult.SlotNotOpen;

		RoomSlotsMechanics.MoveTo(from, to);
		Emit(new RoomPlayerMoved(room, seated, from.Index, to.Index));
		return RoomResult.Ok;
	}

	private RoomResult ToggleSlotLock(Room room, Connection by, int index)
	{
		if (!RoomRules.IsHostOrManager(room, by)) return RoomResult.NotAuthorized;
		if (room.Slots.At(index) is not { } slot) return RoomResult.SlotNotOpen;
		if (ReferenceEquals(slot.Player, by)) return RoomResult.OwnSlot;

		var locking = !slot.Locked;

		BanchoConnection? evicted = null;
		if (locking && slot.Player is { } player)
		{
			evicted = player;
			RoomSlotsMechanics.Vacate(room, player);
		}

		slot.Locked = locking;
		Emit(new RoomSlotLockChanged(room, slot.Index, locking, evicted, room.Host));

		if (evicted is not null)
		{
			LeaveChannel(room, evicted);
			ReportIfEmpty(room);
		}

		return RoomResult.Ok;
	}

	private RoomResult SetLocked(Room room, Connection by, bool locked)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (room.Slots.Locked == locked) return RoomResult.Ok;

		room.Slots.Locked = locked;
		Emit(new RoomLockChanged(room, locked));
		return RoomResult.Ok;
	}

	private RoomResult SetReady(Room room, BanchoConnection by, bool ready)
	{
		if (room.Slots.Find(by) is not { } slot) return RoomResult.NotInRoom;
		if (room.InProgress) return RoomResult.InProgress;

		var status = ready ? RoomSlotStatus.Ready : RoomSlotStatus.NotReady;
		if (slot.Status == status) return RoomResult.Ok;

		RoomSlotsMechanics.SetStatus(slot, status);
		Emit(new RoomSlotStatusChanged(room, slot.Index, status));
		return RoomResult.Ok;
	}

	private RoomResult SetHasMap(Room room, BanchoConnection by, bool has)
	{
		if (room.Slots.Find(by) is not { } slot) return RoomResult.NotInRoom;
		if (slot.Status is RoomSlotStatus.Playing) return RoomResult.Ok;

		if (has && slot.Status is not RoomSlotStatus.NoMap) return RoomResult.Ok;

		var status = has ? RoomSlotStatus.NotReady : RoomSlotStatus.NoMap;
		if (slot.Status == status) return RoomResult.Ok;

		RoomSlotsMechanics.SetStatus(slot, status);
		Emit(new RoomSlotStatusChanged(room, slot.Index, status));
		return RoomResult.Ok;
	}

	private RoomResult ToggleTeam(Room room, BanchoConnection by)
	{
		if (room.Slots.Find(by) is not { } slot) return RoomResult.NotInRoom;
		if (!room.TeamType.NeedSplitTeam()) return RoomResult.NoTeams;
		if (room.Slots.Locked) return RoomResult.RoomLocked;
		if (room.InProgress) return RoomResult.InProgress;

		var team = slot.Team is GameTeam.Red ? GameTeam.Blue : GameTeam.Red;
		slot.Team = team;
		Emit(new RoomSlotTeamChanged(room, slot.Index, team));
		return RoomResult.Ok;
	}

	private RoomResult SetTeam(Room room, Connection by, User player, GameTeam team)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (!room.TeamType.NeedSplitTeam()) return RoomResult.NoTeams;
		if (room.Slots.Find(player) is not { Player: not null } slot) return RoomResult.NotInRoom;
		if (slot.Team == team) return RoomResult.Ok;

		slot.Team = team;
		Emit(new RoomSlotTeamChanged(room, slot.Index, team));
		return RoomResult.Ok;
	}

	private RoomResult SetPlayerMods(Room room, BanchoConnection by, GameMods mods)
	{
		if (room.Slots.Find(by) is not { } slot) return RoomResult.NotInRoom;
		if (room.InProgress) return RoomResult.InProgress;
		if (!room.Freemods) return RoomResult.NotFreemod;
		if ((mods & GameMods.SpeedChangingMods) != GameMods.NoMod) return RoomResult.SpeedModNotAllowed;
		if (!mods.IsValid(room.Mode)) return RoomResult.InvalidMods;
		if (slot.Mods == mods) return RoomResult.Ok;

		slot.Mods = mods;
		Emit(new RoomSlotModsChanged(room, slot.Index, mods));
		return RoomResult.Ok;
	}

	private RoomResult Start(Room room, Connection by)
	{
		if (!RoomRules.IsHostOrManager(room, by)) return RoomResult.NotAuthorized;
		if (room.InProgress) return RoomResult.InProgress;
		if (room.Beatmap is null) return RoomResult.NoBeatmap;

		StopCountdown(room);
		StartRound(room);
		return RoomResult.Ok;
	}

	private RoomResult Abort(Room room, Connection by)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (!room.InProgress) return RoomResult.NotInProgress;

		StopCountdown(room);
		AbortRound(room);
		return RoomResult.Ok;
	}

	private RoomResult MarkLoaded(Room room, BanchoConnection by)
	{
		if (room.CurrentRound is not { } round || room.Slots.Find(by) is not { Status: RoomSlotStatus.Playing } slot)
			return RoomResult.NotPlaying;
		if (slot.Loaded is true) return RoomResult.Ok;

		slot.Loaded = true;
		if (!room.AllLoadedAnnounced &&
		    room.Slots.Where(s => s.Status is RoomSlotStatus.Playing).All(s => s.Loaded is true))
		{
			room.AllLoadedAnnounced = true;
			Emit(new RoomRoundAllLoaded(room, round, slot.Index));
		}
		else
		{
			Emit(new RoomRoundPlayerLoaded(room, round, slot.Index));
		}

		return RoomResult.Ok;
	}

	private RoomResult Skip(Room room, BanchoConnection by)
	{
		if (room.CurrentRound is not { } round || room.Slots.Find(by) is not { Status: RoomSlotStatus.Playing } slot)
			return RoomResult.NotPlaying;
		if (slot.IntroSkipped is true) return RoomResult.Ok;

		slot.IntroSkipped = true;
		if (!room.AllSkippedAnnounced &&
		    room.Slots.Where(s => s.Status is RoomSlotStatus.Playing).All(s => s.IntroSkipped is true))
		{
			room.AllSkippedAnnounced = true;
			Emit(new RoomRoundAllSkipped(room, round, slot.Index));
		}
		else
		{
			Emit(new RoomRoundPlayerSkipped(room, round, slot.Index));
		}

		return RoomResult.Ok;
	}

	private RoomResult Fail(Room room, BanchoConnection by)
	{
		if (room.CurrentRound is not { } round || room.Slots.Find(by) is not { Status: RoomSlotStatus.Playing } slot)
			return RoomResult.NotPlaying;

		Emit(new RoomRoundPlayerFailed(room, round, slot.Index));
		return RoomResult.Ok;
	}

	private RoomResult Complete(Room room, BanchoConnection by)
	{
		if (room.CurrentRound is not { } round || room.Slots.Find(by) is not { Status: RoomSlotStatus.Playing } slot)
			return RoomResult.NotPlaying;

		RoomSlotsMechanics.SetStatus(slot, RoomSlotStatus.Complete);
		if (room.Slots.Any(s => s.Status is RoomSlotStatus.Playing))
			Emit(new RoomRoundPlayerCompleted(room, round, slot.Index));
		else
			EndRound(room, round, slot.Index);
		return RoomResult.Ok;
	}

	private RoomResult StartCountdown(Room room, Connection by, TimeSpan length, bool startsRound)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (length <= TimeSpan.Zero || length > MaxCountdownLength) return RoomResult.OutOfRange;
		if (startsRound && room.InProgress) return RoomResult.InProgress;
		if (startsRound && room.Beatmap is null) return RoomResult.NoBeatmap;

		StopCountdown(room);
		Countdown? countdown = null;
		var milestones = CountdownMarks
			.Where(mark => mark < length)
			.Select(mark => new Countdown.Milestone(mark, _ =>
			{
				Emit(new RoomCountdownTicked(room, mark));
				return Task.CompletedTask;
			}))
			.Append(new Countdown.Milestone(TimeSpan.Zero, _ => ElapseAsync(room, countdown!, startsRound)));
		countdown = new Countdown(length, milestones, time);
		room.CountdownTimer = countdown;
		countdown.Start();
		room.CountdownEndsAt = countdown.EndsAt;
		Emit(new RoomCountdownStarted(room, length, startsRound, countdown.EndsAt!.Value));
		return RoomResult.Ok;
	}

	private RoomResult CancelCountdown(Room room, Connection by)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (room.CountdownTimer is null) return RoomResult.NoCountdown;

		StopCountdown(room);
		Emit(new RoomCountdownCancelled(room));
		return RoomResult.Ok;
	}

	private RoomResult RecordScore(Room room, User player, Score score)
	{
		if (room.LastRound is not { } round || !round.Equals(score.Value.Round)) return RoomResult.RoundMismatch;

		Emit(new RoomRoundScoreSubmitted(room, round, player, score));
		return RoomResult.Ok;
	}

	/// <summary>Applies the room's freemod setting to the room's own mods and every seated player.</summary>
	/// <param name="room">The room to change.</param>
	/// <param name="value">Whether freemod is on.</param>
	/// <remarks>
	///     Turning freemod on hands the room's non-speed-changing mods to every seated player and
	///     leaves the room with only its speed-changing mods. Turning it off gives the room its
	///     speed-changing mods plus the host's own mods, and clears every player's own mods.
	/// </remarks>
	private static void ApplyFreemods(Room room, bool value)
	{
		room.Settings.Freemods = value;

		var seated = room.Slots.Where(s => s.Player is not null).ToList();
		if (value)
		{
			// Players take the room's mods that combine per player; the room keeps only the
			// speed-changing ones, which must stay the same for everyone.
			foreach (var slot in seated)
				slot.Mods = room.Settings.Mods & ~GameMods.SpeedChangingMods;
			room.Settings.Mods &= GameMods.SpeedChangingMods;
		}
		else
		{
			// The room keeps its speed-changing mods and takes the host's own mods.
			var hostMods = (room.Host is { } host ? room.Slots.Find(host)?.Mods : null) ?? GameMods.NoMod;
			room.Settings.Mods = (room.Settings.Mods & GameMods.SpeedChangingMods) | hostMods;
			foreach (var slot in seated)
				slot.Mods = GameMods.NoMod;
		}
	}

	/// <summary>Applies the room's team arrangement, reassigning the teams of the seated players.</summary>
	/// <param name="room">The room to change.</param>
	/// <param name="value">The team arrangement to set.</param>
	private static void ApplyTeamType(Room room, GameTeamType value)
	{
		room.Settings.TeamType = value;

		if (value.NeedSplitTeam())
		{
			var redCount = 0;
			var blueCount = 0;

			foreach (var slot in room.Slots.Where(s => s.Player is not null))
			{
				var team = redCount <= blueCount ? GameTeam.Red : GameTeam.Blue;
				slot.Team = team;

				if (team == GameTeam.Red)
					redCount++;
				else
					blueCount++;
			}
		}
		else
		{
			foreach (var slot in room.Slots.Where(s => s.Player is not null))
				slot.Team = null;
		}
	}

	private void LeaveChannel(Room room, Connection connection)
	{
		if (!RoomRules.IsManager(room, connection.User)) channels.Part(room.Channel, connection);
	}

	private void ReportIfEmpty(Room room)
	{
		AnnounceRoundProgress(room);
		if (!room.Slots.Any(slot => slot.Player is not null)) lobbyService.RoomEmptied(room);
	}

	/// <summary>Reports what the players who are still playing have all done, after a player stopped playing.</summary>
	/// <param name="room">The room whose round to report on.</param>
	/// <remarks>
	///     Ends the round when nobody is playing any more; otherwise announces that every remaining player has loaded or
	///     skipped, once per round.
	/// </remarks>
	private void AnnounceRoundProgress(Room room)
	{
		if (room.CurrentRound is not { } round) return;

		var playing = room.Slots.Where(s => s.Status is RoomSlotStatus.Playing).ToList();
		if (playing.Count == 0)
		{
			EndRound(room, round, null);
			return;
		}

		if (!room.AllLoadedAnnounced && playing.All(s => s.Loaded is true))
		{
			room.AllLoadedAnnounced = true;
			Emit(new RoomRoundAllLoaded(room, round, null));
		}

		if (!room.AllSkippedAnnounced && playing.All(s => s.IntroSkipped is true))
		{
			room.AllSkippedAnnounced = true;
			Emit(new RoomRoundAllSkipped(room, round, null));
		}
	}

	private void StartRound(Room room)
	{
		var round = new Round
		{
			Number = (room.LastRound?.Number ?? 0) + 1,
			Match = room.Match,
			BeatmapHash = room.Beatmap!.Hash,
			Settings = room.Settings.Clone(),
			StartedAt = time.GetUtcNow(),
			EndedAt = null
		};
		room.LastRound = round;
		room.AllLoadedAnnounced = false;
		room.AllSkippedAnnounced = false;

		var players = new List<BanchoConnection>();
		foreach (var slot in room.Slots.Where(s => s.Player is not null && s.Status is not RoomSlotStatus.NoMap))
		{
			RoomSlotsMechanics.SetStatus(slot, RoomSlotStatus.Playing);
			players.Add(slot.Player!);
		}

		Emit(new RoomRoundStarted(room, round, players));
		if (players.Count == 0) EndRound(room, round, null);
	}

	private void AbortRound(Room room)
	{
		var round = room.CurrentRound!;
		round.EndedAt = time.GetUtcNow();
		round.Aborted = true;
		RoomSlotsMechanics.ResetPlayers(room);
		Emit(new RoomRoundAborted(room, round));
	}

	private void EndRound(Room room, Round round, int? slot)
	{
		round.EndedAt = time.GetUtcNow();
		RoomSlotsMechanics.ResetPlayers(room);
		Emit(new RoomRoundCompleted(room, round, slot));
	}

	private static void StopCountdown(Room room)
	{
		room.CountdownTimer?.Dispose();
		room.CountdownTimer = null;
		room.CountdownEndsAt = null;
	}

	private async Task ElapseAsync(Room room, Countdown countdown, bool startsRound)
	{
		await using var scope = await lobby.EnterAsync(room);
		if (scope is null || !ReferenceEquals(room.CountdownTimer, countdown)) return;

		StopCountdown(room);
		Emit(new RoomCountdownElapsed(room, startsRound));
		if (startsRound && !room.InProgress && room.Beatmap is not null) StartRound(room);
	}

	private void Emit(RoomEvent @event)
	{
		_events.Writer.TryWrite(@event);
	}
}
