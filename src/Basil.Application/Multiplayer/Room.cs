using System.Threading.Channels;
using Basil.Domain.Client;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Domain.Utilities;
using Basil.Application.Common.Events;
using Basil.Application.Multiplayer.Events;
using Basil.Application.Sessions;

namespace Basil.Application.Multiplayer;

/// <summary>
///     Holds an osu! multiplayer match's live runtime state: the shared settings that are stored on
///     the underlying <see cref="Match" />, the 16 player slots, and the round lifecycle.
/// </summary>
public sealed class Room : IEventPublisher<RoomEvent>, IEquatable<Room>
{
	/// <summary>The most referees a room can have.</summary>
	public const int MaxReferees = 8;

	/// <summary>The remaining times at which a running countdown is announced.</summary>
	public static readonly TimeSpan[] CountdownMarks =
		[TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)];

	/// <summary>The countdown length used when none is given.</summary>
	public static readonly TimeSpan DefaultCountdownLength = TimeSpan.FromSeconds(30);

	/// <summary>The longest countdown allowed.</summary>
	public static readonly TimeSpan MaxCountdownLength = TimeSpan.FromHours(1);

	private readonly Lobby _lobby;
	private readonly TimeProvider _time;
	private readonly ConcurrentSet<User> _banned = [];
	private readonly ConcurrentSet<User> _referees = [];
	private readonly ConcurrentSet<TourneyConnection> _observers = [];
	private readonly Channel<RoomEvent> _events = System.Threading.Channels.Channel.CreateUnbounded<RoomEvent>();
	private readonly SemaphoreSlim _lock = new(1, 1);
	private BanchoConnection? _host;
	private Countdown? _countdown;
	private bool _closed;

	/// <summary>The match this room is a live projection of.</summary>
	public Match Match { get; }

	/// <summary>The match settings this room mutates; only the forwarding properties are public.</summary>
	private MatchSettings Settings { get; }

	/// <summary>Gets the runtime identifier assigned to this room.</summary>
	public int Id { get; }

	/// <summary>Gets a value that indicates whether the room is a tournament room, which stays open for a while when empty.</summary>
	public bool IsTournament { get; }

	/// <summary>Gets the room's chat channel.</summary>
	public RoomChatChannelSession Channel { get; }

	/// <summary>The match's 16 slots, in order.</summary>
	public RoomSlots Slots { get; }

	public ChannelReader<RoomEvent> Events => _events.Reader;

	/// <summary>Gets the name broadcast to clients.</summary>
	public string Name => Match.Value.Name;

	/// <summary>Gets a value that indicates whether the room's match history is private.</summary>
	public bool IsPrivate => Match.Value.IsPrivate;

	/// <summary>Gets the user who created the match.</summary>
	public User? Creator => Match.Value.Creator;

	/// <summary>Gets the room's password.</summary>
	private string Password { get; set; } = string.Empty;

	/// <summary>Gets the currently selected beatmap.</summary>
	public BeatmapReference? Beatmap { get; private set; }

	/// <summary>Gets the game mode played in the room.</summary>
	public GameMode Mode => Settings.Mode;

	/// <summary>Gets the mods applied to the whole room.</summary>
	public GameMods Mods => Settings.Mods;

	/// <summary>Gets a value that indicates whether freemod mode is enabled.</summary>
	public bool Freemods => Settings.Freemods;

	/// <summary>Gets the team arrangement used for the room.</summary>
	public GameTeamType TeamType => Settings.TeamType;

	/// <summary>Gets the condition that decides the winner of a round.</summary>
	public GameWinCondition WinCondition => Settings.WinCondition;

	/// <summary>Gets the connection currently hosting the room.</summary>
	/// <remarks>The host must be seated in this room.</remarks>
	public BanchoConnection? Host => _host;

	/// <summary>Gets the most recently started round, or <see langword="null" /> before the first round.</summary>
	/// <remarks>Stays set after the round ends, until the next round starts.</remarks>
	public Round? LastRound { get; private set; }

	/// <summary>Gets the round currently being played, or <see langword="null" /> when none is in progress.</summary>
	public Round? CurrentRound => LastRound is { EndedAt: null } round ? round : null;

	/// <summary>Gets a value that indicates whether a round is currently in progress.</summary>
	public bool InProgress => CurrentRound is not null;

	/// <summary>Gets when the running countdown ends, or <see langword="null" /> when none is running.</summary>
	public DateTimeOffset? CountdownEndsAt => _countdown?.EndsAt;

	/// <summary>Gets the users with referee authority for this room.</summary>
	public IReadOnlySet<User> Referees => _referees;

	/// <summary>Gets the users banned from this room.</summary>
	public IReadOnlySet<User> Banned => _banned;

	/// <summary>Gets the osu!tourney clients observing the room.</summary>
	public IReadOnlySet<TourneyConnection> Observers => _observers;

	/// <summary>Gets the join URL of this room, in <c>osump://{id}/{password}</c> form.</summary>
	public string Url => $"osump://{Id}/{Password}";

	/// <summary>
	///     Gets an osu! chat embed for this room, formatted as a clickable name linked to <see cref="Url" />.
	/// </summary>
	public string UrlEmbed => $"({Match.Value.Name})[{Url}]";

	/// <summary>Opens a room for a match.</summary>
	/// <param name="lobby">The lobby that owns this room.</param>
	/// <param name="time">The clock the room's rounds and countdown run on.</param>
	/// <param name="id">The room id, carried by the client protocol.</param>
	/// <param name="match">The match the room plays.</param>
	/// <param name="settings">The room's initial settings.</param>
	/// <param name="isTournament">Whether the room is a tournament room.</param>
	internal Room(Lobby lobby, TimeProvider time, int id, Match match, MatchSettings settings, bool isTournament)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(id);
		_lobby = lobby;
		_time = time;
		Id = id;
		Match = match;
		Settings = settings;
		IsTournament = isTournament;
		Slots = new RoomSlots(this);
		Channel = new RoomChatChannelSession(this);
	}

	/// <summary>Enters the room's exclusive scope; every operation on the room runs inside it.</summary>
	/// <param name="cancellationToken">A token that cancels the wait.</param>
	/// <returns>The scope, to dispose when done; or <see langword="null" /> when the room is closed.</returns>
	public async Task<IAsyncDisposable?> EnterAsync(CancellationToken cancellationToken = default)
	{
		await _lock.WaitAsync(cancellationToken);
		if (!_closed) return new Scope(_lock);
		_lock.Release();
		return null;
	}

	/// <summary>Gets a value that indicates whether a user is the creator or a referee of the room.</summary>
	/// <param name="user">The user to check.</param>
	/// <returns>
	///     <see langword="true" /> if the user is the creator or a referee of this match;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool IsManager(User user)
	{
		return _referees.Contains(user) || (Creator is not null && Creator.Equals(user));
	}

	/// <summary>Seats a player in the first open slot.</summary>
	/// <param name="by">The joining player's game client.</param>
	/// <param name="password">The password the player supplied.</param>
	/// <returns>Ok, AlreadySeated, Banned, Silenced, NotAuthorized, InAnotherRoom, IsObserver, WrongPassword or Full.</returns>
	/// <remarks>The caller holds the room's scope. If the same user is seated through a connection that has closed, that connection leaves first. Moderators need no password. The player also joins the room's chat channel and stops watching the lobby.</remarks>
	public RoomResult Join(BanchoConnection by, string password)
	{
		if (Slots.Find(by.User) is { Player: { } seated })
		{
			if (ReferenceEquals(seated, by) || seated.IsOpen) return RoomResult.AlreadySeated;

			// The old connection leaves as an ordinary leave, but the room is not reported empty: the user is coming back.
			var vacated = Slots.Vacate(seated)!;
			Emit(new PlayerLeft(this, seated, vacated.Index, _host));
			LeaveChannel(seated);
			EndRoundIfNobodyPlays();
		}

		if (_banned.Contains(by.User)) return RoomResult.Banned;
		if (by.User.Value.SilenceEndsAt > DateTimeOffset.UtcNow) return RoomResult.Silenced;
		if (!by.User.Value.Privilege.Has(ClientPrivileges.Player)) return RoomResult.NotAuthorized;
		if (_lobby.RoomOf(by) is { } other && !ReferenceEquals(other, this)) return RoomResult.InAnotherRoom;
		if (_observers.Any(observer => observer.User.Equals(by.User))) return RoomResult.IsObserver;
		if (!string.IsNullOrEmpty(Password) && Password != password &&
		    !by.User.Value.Privilege.Has(ClientPrivileges.Moderator))
			return RoomResult.WrongPassword;
		if (Slots.Seat(by) is null) return RoomResult.Full;

		Channel.Join(by);
		_lobby.Unwatch(by);
		_lobby.RoomOccupied(this);
		return RoomResult.Ok;
	}

	/// <summary>Removes a player from the room.</summary>
	/// <param name="by">The leaving player's game client.</param>
	/// <returns>Ok or NotInRoom; leaving again returns NotInRoom and does nothing.</returns>
	/// <remarks>The caller holds the room's scope. If the host leaves, the next seated player by slot order becomes host. A room left empty is closed by the lobby.</remarks>
	public RoomResult Leave(BanchoConnection by)
	{
		if (Slots.Vacate(by) is not { } slot) return RoomResult.NotInRoom;

		Emit(new PlayerLeft(this, by, slot.Index, _host));
		LeaveChannel(by);
		ReportIfEmpty();
		return RoomResult.Ok;
	}

	/// <summary>Removes another player from the room.</summary>
	/// <param name="by">The host or a manager.</param>
	/// <param name="player">The user to remove.</param>
	/// <returns>Ok, NotAuthorized, IsManager or NotInRoom.</returns>
	/// <remarks>The caller holds the room's scope. The creator and referees cannot be kicked.</remarks>
	public RoomResult Kick(Connection by, User player)
	{
		if (!IsHostOrManager(by)) return RoomResult.NotAuthorized;
		if (IsManager(player)) return RoomResult.IsManager;
		if (Slots.Find(player) is not { Player: { } seated }) return RoomResult.NotInRoom;

		var slot = Slots.Vacate(seated)!;
		Emit(new PlayerKicked(this, seated, slot.Index, _host));
		LeaveChannel(seated);
		ReportIfEmpty();
		return RoomResult.Ok;
	}

	/// <summary>Bans a user from the room, removing them if seated.</summary>
	/// <param name="by">A manager.</param>
	/// <param name="player">The user to ban.</param>
	/// <returns>Ok, NotAuthorized or IsManager; banning a banned user again returns Ok and does nothing.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	public RoomResult Ban(Connection by, User player)
	{
		if (!IsManager(by.User)) return RoomResult.NotAuthorized;
		if (IsManager(player)) return RoomResult.IsManager;
		if (!_banned.Add(player)) return RoomResult.Ok;

		int? vacated = null;
		BanchoConnection? evicted = null;
		if (Slots.Find(player) is { Player: { } seated })
		{
			evicted = seated;
			vacated = Slots.Vacate(seated)?.Index;
		}

		Emit(new PlayerBanned(this, player, vacated, evicted, _host));
		if (evicted is not null)
		{
			LeaveChannel(evicted);
			ReportIfEmpty();
		}

		return RoomResult.Ok;
	}

	/// <summary>Lifts a user's ban.</summary>
	/// <param name="by">A manager.</param>
	/// <param name="player">The banned user.</param>
	/// <returns>Ok, NotAuthorized or NotBanned.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	public RoomResult Unban(Connection by, User player)
	{
		if (!IsManager(by.User)) return RoomResult.NotAuthorized;
		if (!_banned.Remove(player)) return RoomResult.NotBanned;

		Emit(new PlayerUnbanned(this, player));
		return RoomResult.Ok;
	}

	/// <summary>Invites an online user to the room.</summary>
	/// <param name="by">A seated player or a manager.</param>
	/// <param name="target">The online session of the invited user.</param>
	/// <returns>Ok, NotAuthorized, TargetOffline or AlreadyInRoom.</returns>
	/// <remarks>The caller holds the room's scope. The server's bot cannot be invited.</remarks>
	public RoomResult Invite(Connection by, UserSession target)
	{
		var seated = by is BanchoConnection player && Slots.Find(player) is not null;
		if (!seated && !IsManager(by.User)) return RoomResult.NotAuthorized;
		if (target.Bot is not null || !target.Connections.Any(connection => connection.IsOpen))
			return RoomResult.TargetOffline;
		if (Slots.Find(target.User) is not null) return RoomResult.AlreadyInRoom;

		Emit(new PlayerInvited(this, by.User, target.User));
		return RoomResult.Ok;
	}

	/// <summary>Makes a user a referee.</summary>
	/// <param name="by">The creator.</param>
	/// <param name="user">The user to make referee.</param>
	/// <returns>Ok, NotAuthorized, IsCreator, AlreadyReferee or TooManyReferees.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	public RoomResult AddReferee(Connection by, User user)
	{
		if (Creator is null || !Creator.Equals(by.User)) return RoomResult.NotAuthorized;
		if (Creator.Equals(user)) return RoomResult.IsCreator;
		if (_referees.Contains(user)) return RoomResult.AlreadyReferee;
		if (_referees.Count >= MaxReferees) return RoomResult.TooManyReferees;

		_referees.Add(user);
		Emit(new RefereeAdded(this, user));
		return RoomResult.Ok;
	}

	/// <summary>Removes a user from the referees.</summary>
	/// <param name="by">The creator.</param>
	/// <param name="user">The referee to remove.</param>
	/// <returns>Ok, NotAuthorized or NotReferee.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	public RoomResult RemoveReferee(Connection by, User user)
	{
		if (Creator is null || !Creator.Equals(by.User)) return RoomResult.NotAuthorized;
		if (!_referees.Remove(user)) return RoomResult.NotReferee;

		Emit(new RefereeRemoved(this, user));
		return RoomResult.Ok;
	}

	/// <summary>Gives host to a seated player, or clears it.</summary>
	/// <param name="by">The host or a manager.</param>
	/// <param name="host">The seated player to make host, or <see langword="null" /> to clear the host.</param>
	/// <returns>Ok, NotAuthorized or NotInRoom.</returns>
	/// <remarks>The caller holds the room's scope. Giving host to the current host does nothing.</remarks>
	public RoomResult SetHost(Connection by, BanchoConnection? host)
	{
		if (!IsHostOrManager(by)) return RoomResult.NotAuthorized;
		if (host is not null && Slots.Find(host) is null) return RoomResult.NotInRoom;
		if (ReferenceEquals(_host, host)) return RoomResult.Ok;

		_host = host;
		Emit(new HostChanged(this, host));
		return RoomResult.Ok;
	}

	/// <summary>Starts observing the room from an osu!tourney client.</summary>
	/// <param name="by">The osu!tourney client.</param>
	/// <returns>Ok or IsPlayer; observing again returns Ok and does nothing.</returns>
	/// <remarks>The caller holds the room's scope. The observer also joins the room's chat channel.</remarks>
	public RoomResult ObserverJoin(TourneyConnection by)
	{
		if (Slots.Find(by.User) is not null) return RoomResult.IsPlayer;
		if (!_observers.Add(by)) return RoomResult.Ok;

		Emit(new ObserverJoined(this, by));
		Channel.Join(by);
		return RoomResult.Ok;
	}

	/// <summary>Stops observing the room.</summary>
	/// <param name="by">The osu!tourney client.</param>
	/// <returns>Ok or NotObserver.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	public RoomResult ObserverLeave(TourneyConnection by)
	{
		if (!_observers.Remove(by)) return RoomResult.NotObserver;

		Emit(new ObserverLeft(this, by));
		LeaveChannel(by);
		return RoomResult.Ok;
	}

	/// <summary>Changes the room's settings in one step.</summary>
	/// <param name="by">The host or a manager.</param>
	/// <param name="change">The settings to change.</param>
	/// <returns>Ok, NotAuthorized, InProgress, InvalidSettings or InvalidMods.</returns>
	/// <remarks>
	///     The caller holds the room's scope. Nothing changes unless every field is valid. Selecting a
	///     beatmap sets ready players back to not ready. Turning freemod on moves the room's mods that are
	///     not speed-changing onto each player; turning it off gives the room the host's mods. Changing the
	///     team type reassigns teams. Changing the mode drops mods the new mode does not allow.
	/// </remarks>
	public RoomResult Configure(Connection by, RoomSettingsChange change)
	{
		if (!IsHostOrManager(by)) return RoomResult.NotAuthorized;
		if (InProgress) return RoomResult.InProgress;

		if (change.Name is not null && string.IsNullOrWhiteSpace(change.Name)) return RoomResult.InvalidSettings;
		if (change.Size is < 1 or > RoomSlots.MaxSlotCount) return RoomResult.InvalidSettings;
		if (change.Mode is { } requestedMode && !Enum.IsDefined(requestedMode)) return RoomResult.InvalidSettings;
		if (change.TeamType is { } requestedTeamType && !Enum.IsDefined(requestedTeamType))
			return RoomResult.InvalidSettings;
		if (change.WinCondition is { } requestedWinCondition && !Enum.IsDefined(requestedWinCondition))
			return RoomResult.InvalidSettings;

		var mode = change.Mode ?? Mode;
		var freemods = change.Freemods ?? Freemods;
		if (change.Mods is { } requestedMods && !requestedMods.IsValid(mode)) return RoomResult.InvalidMods;

		if (change.Name is { } name) Match.Value.Name = name;
		if (change.Password is { } password) Password = password;
		if (change.Beatmap is { } beatmap)
		{
			Beatmap = beatmap;
			foreach (var slot in Slots.Where(s => s.Status is RoomSlotStatus.Ready))
				slot.SetStatus(RoomSlotStatus.NotReady);
		}

		if (change.Mode is { } newMode) Settings.SwitchMode(newMode);
		if (change.Freemods is { } newFreemods && newFreemods != Freemods) ApplyFreemods(newFreemods);
		if (change.Mods is { } newMods) Settings.Mods = freemods ? newMods & GameMods.SpeedChangingMods : newMods;
		if (change.TeamType is { } teamType) ApplyTeamType(teamType);
		if (change.WinCondition is { } winCondition) Settings.WinCondition = winCondition;
		if (change.Size is { } size) Slots.Resize(size);

		Emit(new RoomSettingsChanged(this, change with { Password = null }, change.Password is not null));
		return RoomResult.Ok;
	}

	/// <summary>Moves the caller to another open slot.</summary>
	/// <param name="by">The caller's game client.</param>
	/// <param name="index">The slot number, from 1 to 16.</param>
	/// <returns>Ok, NotInRoom, RoomLocked, InProgress or SlotNotOpen.</returns>
	/// <remarks>The caller holds the room's scope. Moving to the caller's own slot returns Ok and does nothing.</remarks>
	public RoomResult ChangeSlot(BanchoConnection by, int index)
	{
		if (Slots.Find(by) is not { } from) return RoomResult.NotInRoom;
		if (Slots.Locked) return RoomResult.RoomLocked;
		if (InProgress) return RoomResult.InProgress;
		if (Slots.At(index) is not { } to) return RoomResult.SlotNotOpen;
		if (ReferenceEquals(from, to)) return RoomResult.Ok;
		if (to.Locked || to.Player is not null) return RoomResult.SlotNotOpen;

		from.MoveTo(to);
		Emit(new PlayerMoved(this, by, from.Index, to.Index));
		return RoomResult.Ok;
	}

	/// <summary>Moves a player to an empty, unlocked slot.</summary>
	/// <param name="by">The caller's connection.</param>
	/// <param name="player">The user to act on.</param>
	/// <param name="index">The slot number, from 1 to 16.</param>
	/// <returns>Ok, NotAuthorized, NotInRoom or SlotNotOpen.</returns>
	/// <remarks>The caller holds the room's scope. This is a referee operation, so it is allowed while the room is locked.</remarks>
	public RoomResult Move(Connection by, User player, int index)
	{
		if (!IsManager(by.User)) return RoomResult.NotAuthorized;
		if (Slots.Find(player) is not { Player: { } seated } from) return RoomResult.NotInRoom;
		if (Slots.At(index) is not { } to) return RoomResult.SlotNotOpen;
		if (ReferenceEquals(from, to)) return RoomResult.Ok;
		if (to.Locked || to.Player is not null) return RoomResult.SlotNotOpen;

		from.MoveTo(to);
		Emit(new PlayerMoved(this, seated, from.Index, to.Index));
		return RoomResult.Ok;
	}

	/// <summary>Locks or unlocks a slot; locking an occupied slot removes its player.</summary>
	/// <param name="by">The caller's connection.</param>
	/// <param name="index">The slot number, from 1 to 16.</param>
	/// <returns>Ok, NotAuthorized, SlotNotOpen or OwnSlot.</returns>
	/// <remarks>The caller holds the room's scope. A player cannot lock the slot they occupy.</remarks>
	public RoomResult ToggleSlotLock(Connection by, int index)
	{
		if (!IsHostOrManager(by)) return RoomResult.NotAuthorized;
		if (Slots.At(index) is not { } slot) return RoomResult.SlotNotOpen;
		if (ReferenceEquals(slot.Player, by)) return RoomResult.OwnSlot;

		var locking = !slot.Locked;

		BanchoConnection? evicted = null;
		if (locking && slot.Player is { } player)
		{
			evicted = player;
			Slots.Vacate(player);
		}

		slot.SetLocked(locking);
		Emit(new SlotLockChanged(this, slot.Index, locking, evicted, _host));

		if (evicted is not null)
		{
			LeaveChannel(evicted);
			ReportIfEmpty();
		}

		return RoomResult.Ok;
	}

	/// <summary>Locks or unlocks the room, which stops players from changing slot or team.</summary>
	/// <param name="by">The caller's connection.</param>
	/// <param name="locked">The lock state to set.</param>
	/// <returns>Ok or NotAuthorized; setting the current state again returns Ok and does nothing.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	public RoomResult SetLocked(Connection by, bool locked)
	{
		if (!IsManager(by.User)) return RoomResult.NotAuthorized;
		if (Slots.Locked == locked) return RoomResult.Ok;

		Slots.Locked = locked;
		Emit(new RoomLockChanged(this, locked));
		return RoomResult.Ok;
	}

	/// <summary>Marks the caller ready or not ready.</summary>
	/// <param name="by">The caller's game client.</param>
	/// <param name="ready">Whether the caller is ready to play.</param>
	/// <returns>Ok, NotInRoom or InProgress.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	public RoomResult SetReady(BanchoConnection by, bool ready)
	{
		if (Slots.Find(by) is not { } slot) return RoomResult.NotInRoom;
		if (InProgress) return RoomResult.InProgress;

		var status = ready ? RoomSlotStatus.Ready : RoomSlotStatus.NotReady;
		if (slot.Status == status) return RoomResult.Ok;

		slot.SetStatus(status);
		Emit(new SlotStatusChanged(this, slot.Index, status));
		return RoomResult.Ok;
	}

	/// <summary>Reports whether the caller has the selected beatmap.</summary>
	/// <param name="by">The caller's game client.</param>
	/// <param name="has">Whether the caller has the selected beatmap.</param>
	/// <returns>Ok or NotInRoom.</returns>
	/// <remarks>The caller holds the room's scope. The report is ignored while the caller is playing.</remarks>
	public RoomResult SetHasMap(BanchoConnection by, bool has)
	{
		if (Slots.Find(by) is not { } slot) return RoomResult.NotInRoom;
		if (slot.Status is RoomSlotStatus.Playing) return RoomResult.Ok;

		var status = has ? RoomSlotStatus.NotReady : RoomSlotStatus.NoMap;
		if (slot.Status == status) return RoomResult.Ok;

		slot.SetStatus(status);
		Emit(new SlotStatusChanged(this, slot.Index, status));
		return RoomResult.Ok;
	}

	/// <summary>Switches the caller to the other team.</summary>
	/// <param name="by">The caller's game client.</param>
	/// <returns>Ok, NotInRoom, NoTeams, RoomLocked or InProgress.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	public RoomResult ToggleTeam(BanchoConnection by)
	{
		if (Slots.Find(by) is not { } slot) return RoomResult.NotInRoom;
		if (!TeamType.NeedSplitTeam()) return RoomResult.NoTeams;
		if (Slots.Locked) return RoomResult.RoomLocked;
		if (InProgress) return RoomResult.InProgress;

		var team = slot.Team is GameTeam.Red ? GameTeam.Blue : GameTeam.Red;
		slot.SetTeam(team);
		Emit(new SlotTeamChanged(this, slot.Index, team));
		return RoomResult.Ok;
	}

	/// <summary>Puts a player on a team.</summary>
	/// <param name="by">The caller's connection.</param>
	/// <param name="player">The user to act on.</param>
	/// <param name="team">The team to assign.</param>
	/// <returns>Ok, NotAuthorized, NoTeams or NotInRoom.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	public RoomResult SetTeam(Connection by, User player, GameTeam team)
	{
		if (!IsManager(by.User)) return RoomResult.NotAuthorized;
		if (!TeamType.NeedSplitTeam()) return RoomResult.NoTeams;
		if (Slots.Find(player) is not { Player: not null } slot) return RoomResult.NotInRoom;
		if (slot.Team == team) return RoomResult.Ok;

		slot.SetTeam(team);
		Emit(new SlotTeamChanged(this, slot.Index, team));
		return RoomResult.Ok;
	}

	/// <summary>Chooses the caller's own mods while freemod is on.</summary>
	/// <param name="by">The caller's game client.</param>
	/// <param name="mods">The mods to select.</param>
	/// <returns>Ok, NotInRoom, InProgress, NotFreemod, SpeedModNotAllowed or InvalidMods.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	public RoomResult SetPlayerMods(BanchoConnection by, GameMods mods)
	{
		if (Slots.Find(by) is not { } slot) return RoomResult.NotInRoom;
		if (InProgress) return RoomResult.InProgress;
		if (!Freemods) return RoomResult.NotFreemod;
		if ((mods & GameMods.SpeedChangingMods) != GameMods.NoMod) return RoomResult.SpeedModNotAllowed;
		if (!mods.IsValid(Mode)) return RoomResult.InvalidMods;
		if (slot.Mods == mods) return RoomResult.Ok;

		slot.SetMods(mods);
		Emit(new SlotModsChanged(this, slot.Index, mods));
		return RoomResult.Ok;
	}

	/// <summary>Sets the room's password when the lobby opens the room.</summary>
	/// <param name="password">The password the room opens with.</param>
	internal void SetInitialPassword(string password)
	{
		Password = password;
	}

	/// <summary>Seats the creator's game client as the first host when the lobby opens the room.</summary>
	internal void SeatCreator(BanchoConnection creator)
	{
		if (Slots.Seat(creator) is null) return;
		_host = creator;
		Channel.Join(creator);
	}

	/// <summary>Passes host to the next seated player by slot order when the host's slot is emptied.</summary>
	internal void PassHostFrom(BanchoConnection leaving)
	{
		if (!ReferenceEquals(_host, leaving)) return;
		_host = Slots.FirstOrDefault(slot => slot.Player is not null && !ReferenceEquals(slot.Player, leaving))?.Player;
	}

	/// <summary>Starts the next round; players who have the beatmap start playing.</summary>
	/// <param name="by">The host or a manager.</param>
	/// <returns>Ok, NotAuthorized, InProgress or NoBeatmap.</returns>
	/// <remarks>The caller holds the room's scope. A running countdown is cancelled without a separate event.</remarks>
	public RoomResult Start(Connection by)
	{
		if (!IsHostOrManager(by)) return RoomResult.NotAuthorized;
		if (InProgress) return RoomResult.InProgress;
		if (Beatmap is null) return RoomResult.NoBeatmap;

		StopCountdown();
		StartRound();
		return RoomResult.Ok;
	}

	/// <summary>Aborts the round in progress; players go back to not ready.</summary>
	/// <param name="by">A manager.</param>
	/// <returns>Ok, NotAuthorized or NotInProgress.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	public RoomResult Abort(Connection by)
	{
		if (!IsManager(by.User)) return RoomResult.NotAuthorized;
		if (!InProgress) return RoomResult.NotInProgress;

		StopCountdown();
		AbortRound();
		return RoomResult.Ok;
	}

	/// <summary>Reports that the caller finished loading the beatmap.</summary>
	/// <param name="by">The caller's game client.</param>
	/// <returns>Ok or NotPlaying; reporting again returns Ok and does nothing.</returns>
	/// <remarks>The caller holds the room's scope. The last player to load emits <see cref="AllPlayersLoaded" /> instead of <see cref="PlayerLoaded" />.</remarks>
	public RoomResult MarkLoaded(BanchoConnection by)
	{
		if (CurrentRound is not { } round || Slots.Find(by) is not { Status: RoomSlotStatus.Playing } slot) return RoomResult.NotPlaying;
		if (slot.Loaded is true) return RoomResult.Ok;

		slot.SetLoaded(true);
		Emit(Slots.Where(s => s.Status is RoomSlotStatus.Playing).All(s => s.Loaded is true)
			? new AllPlayersLoaded(this, round, slot.Index)
			: new PlayerLoaded(this, round, slot.Index));
		return RoomResult.Ok;
	}

	/// <summary>Reports that the caller wants to skip the intro.</summary>
	/// <param name="by">The caller's game client.</param>
	/// <returns>Ok or NotPlaying; asking again returns Ok and does nothing.</returns>
	/// <remarks>The caller holds the room's scope. The last player to ask emits <see cref="AllPlayersSkipped" /> instead of <see cref="PlayerSkipped" />.</remarks>
	public RoomResult Skip(BanchoConnection by)
	{
		if (CurrentRound is not { } round || Slots.Find(by) is not { Status: RoomSlotStatus.Playing } slot) return RoomResult.NotPlaying;
		if (slot.IntroSkipped is true) return RoomResult.Ok;

		slot.SetIntroSkipped(true);
		Emit(Slots.Where(s => s.Status is RoomSlotStatus.Playing).All(s => s.IntroSkipped is true)
			? new AllPlayersSkipped(this, round, slot.Index)
			: new PlayerSkipped(this, round, slot.Index));
		return RoomResult.Ok;
	}

	/// <summary>Reports that the caller failed; the player keeps playing until completion.</summary>
	/// <param name="by">The caller's game client.</param>
	/// <returns>Ok or NotPlaying.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	public RoomResult Fail(BanchoConnection by)
	{
		if (CurrentRound is not { } round || Slots.Find(by) is not { Status: RoomSlotStatus.Playing } slot) return RoomResult.NotPlaying;

		Emit(new PlayerFailed(this, round, slot.Index));
		return RoomResult.Ok;
	}

	/// <summary>Reports that the caller completed the beatmap.</summary>
	/// <param name="by">The caller's game client.</param>
	/// <returns>Ok or NotPlaying.</returns>
	/// <remarks>The caller holds the room's scope. When the last player completes, the round ends and <see cref="RoundCompleted" /> is emitted instead of <see cref="PlayerCompleted" />.</remarks>
	public RoomResult Complete(BanchoConnection by)
	{
		if (CurrentRound is not { } round || Slots.Find(by) is not { Status: RoomSlotStatus.Playing } slot) return RoomResult.NotPlaying;

		slot.SetStatus(RoomSlotStatus.Complete);
		if (Slots.Any(s => s.Status is RoomSlotStatus.Playing))
			Emit(new PlayerCompleted(this, round, slot.Index));
		else
			EndRound(round, slot.Index);
		return RoomResult.Ok;
	}

	/// <summary>Starts a countdown, replacing any running one.</summary>
	/// <param name="by">A manager.</param>
	/// <param name="length">The countdown length, more than zero and at most <see cref="MaxCountdownLength" />.</param>
	/// <param name="startsRound">Whether the round starts when the countdown ends.</param>
	/// <returns>Ok, NotAuthorized, OutOfRange or InProgress.</returns>
	/// <remarks>The caller holds the room's scope. The countdown is announced at each of <see cref="CountdownMarks" /> shorter than its length.</remarks>
	public RoomResult StartCountdown(Connection by, TimeSpan length, bool startsRound)
	{
		if (!IsManager(by.User)) return RoomResult.NotAuthorized;
		if (length <= TimeSpan.Zero || length > MaxCountdownLength) return RoomResult.OutOfRange;
		if (startsRound && InProgress) return RoomResult.InProgress;

		StopCountdown();
		Countdown? countdown = null;
		var milestones = CountdownMarks
			.Where(mark => mark < length)
			.Select(mark => new Countdown.Milestone(mark, _ =>
			{
				Emit(new CountdownTick(this, mark));
				return Task.CompletedTask;
			}))
			.Append(new Countdown.Milestone(TimeSpan.Zero, _ => ElapseAsync(countdown!, startsRound)));
		countdown = new Countdown(length, milestones, _time);
		_countdown = countdown;
		countdown.Start();
		Emit(new CountdownStarted(this, length, startsRound, countdown.EndsAt!.Value));
		return RoomResult.Ok;
	}

	/// <summary>Cancels the running countdown.</summary>
	/// <param name="by">A manager.</param>
	/// <returns>Ok, NotAuthorized or NoCountdown.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	public RoomResult CancelCountdown(Connection by)
	{
		if (!IsManager(by.User)) return RoomResult.NotAuthorized;
		if (_countdown is null) return RoomResult.NoCountdown;

		StopCountdown();
		Emit(new CountdownCancelled(this));
		return RoomResult.Ok;
	}

	/// <summary>Closes the room, ending the match and vacating every slot.</summary>
	/// <returns>The connections that were seated when the room closed.</returns>
	internal IReadOnlyList<BanchoConnection> Close()
	{
		if (_closed) return [];
		StopCountdown();
		if (InProgress) AbortRound();

		var evicted = Slots.Where(s => s.Player is not null).Select(s => s.Player!).ToList();
		foreach (var player in evicted) Slots.Vacate(player);

		Match.Value.EndedAt = DateTimeOffset.UtcNow;
		_observers.Clear();
		_closed = true;
		Channel.Close();
		_events.Writer.TryComplete();
		return evicted;
	}

	/// <summary>Writes an event to the room's event channel.</summary>
	/// <param name="event">The event to emit.</param>
	internal void Emit(RoomEvent @event)
	{
		_events.Writer.TryWrite(@event);
	}

	/// <summary>Records a stored score of the room's latest round.</summary>
	/// <param name="player">The player who submitted the score.</param>
	/// <param name="score">The stored score.</param>
	/// <returns>Ok, or RoundMismatch when the score was not played in the latest round.</returns>
	/// <remarks>The caller holds the room's scope.</remarks>
	public RoomResult RecordScore(User player, Score score)
	{
		if (LastRound is not { } round || !round.Equals(score.Value.Round)) return RoomResult.RoundMismatch;

		Emit(new ScoreSubmitted(this, round, player, score));
		return RoomResult.Ok;
	}

	/// <inheritdoc />
	public bool Equals(Room? other)
	{
		return other is not null && Match.Equals(other.Match);
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is Room other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return Match.GetHashCode();
	}

	private bool IsHostOrManager(Connection by) => ReferenceEquals(by, _host) || IsManager(by.User);

	/// <summary>Applies the room's freemod setting to the room's own mods and every seated player.</summary>
	/// <param name="value">Whether freemod is on.</param>
	/// <remarks>
	///     Turning freemod on hands the room's non-speed-changing mods to every seated player and
	///     leaves the room with only its speed-changing mods. Turning it off gives the room its
	///     speed-changing mods plus the host's own mods, and clears every player's own mods.
	/// </remarks>
	private void ApplyFreemods(bool value)
	{
		Settings.Freemods = value;

		var seated = Slots.Where(s => s.Player is not null).ToList();
		if (value)
		{
			// Players take the room's mods that combine per player; the room keeps only the
			// speed-changing ones, which must stay the same for everyone.
			foreach (var slot in seated)
				slot.SetMods(Settings.Mods & ~GameMods.SpeedChangingMods);
			Settings.Mods &= GameMods.SpeedChangingMods;
		}
		else
		{
			// The room keeps its speed-changing mods and takes the host's own mods.
			var hostMods = (Host is { } host ? Slots.Find(host)?.Mods : null) ?? GameMods.NoMod;
			Settings.Mods = (Settings.Mods & GameMods.SpeedChangingMods) | hostMods;
			foreach (var slot in seated)
				slot.SetMods(GameMods.NoMod);
		}
	}

	/// <summary>Applies the room's team arrangement, reassigning the teams of the seated players.</summary>
	/// <param name="value">The team arrangement to set.</param>
	private void ApplyTeamType(GameTeamType value)
	{
		Settings.TeamType = value;

		if (value.NeedSplitTeam())
		{
			var redCount = 0;
			var blueCount = 0;

			foreach (var slot in Slots.Where(s => s.Player is not null))
			{
				var team = redCount <= blueCount ? GameTeam.Red : GameTeam.Blue;
				slot.SetTeam(team);

				if (team == GameTeam.Red)
					redCount++;
				else
					blueCount++;
			}
		}
		else
		{
			foreach (var slot in Slots.Where(s => s.Player is not null))
				slot.SetTeam(null);
		}
	}

	private void LeaveChannel(Connection connection)
	{
		if (!IsManager(connection.User)) Channel.Part(connection);
	}

	private void ReportIfEmpty()
	{
		EndRoundIfNobodyPlays();
		if (!Slots.Any(slot => slot.Player is not null)) _lobby.RoomEmptied(this);
	}

	private void EndRoundIfNobodyPlays()
	{
		if (CurrentRound is { } round && !Slots.Any(s => s.Status is RoomSlotStatus.Playing)) EndRound(round, null);
	}

	private void StartRound()
	{
		var round = new Round
		{
			Number = (LastRound?.Number ?? 0) + 1,
			Match = Match,
			BeatmapHash = Beatmap!.Hash,
			Settings = Settings.Clone(),
			StartedAt = _time.GetUtcNow(),
			EndedAt = null
		};
		LastRound = round;

		var players = new List<BanchoConnection>();
		foreach (var slot in Slots.Where(s => s.Player is not null && s.Status is not RoomSlotStatus.NoMap))
		{
			slot.SetStatus(RoomSlotStatus.Playing);
			players.Add(slot.Player!);
		}

		Emit(new RoundStarted(this, round, players));
	}

	private void AbortRound()
	{
		var round = CurrentRound!;
		round.EndedAt = _time.GetUtcNow();
		round.Aborted = true;
		ResetPlayers();
		Emit(new RoundAborted(this, round));
	}

	private void EndRound(Round round, int? slot)
	{
		round.EndedAt = _time.GetUtcNow();
		ResetPlayers();
		Emit(new RoundCompleted(this, round, slot));
	}

	private void ResetPlayers()
	{
		foreach (var slot in Slots.Where(s => s.Status is RoomSlotStatus.Playing or RoomSlotStatus.Complete))
			slot.SetStatus(RoomSlotStatus.NotReady);
	}

	private void StopCountdown()
	{
		_countdown?.Dispose();
		_countdown = null;
	}

	private async Task ElapseAsync(Countdown countdown, bool startsRound)
	{
		await using var scope = await EnterAsync();
		if (scope is null || !ReferenceEquals(_countdown, countdown)) return;

		StopCountdown();
		Emit(new CountdownElapsed(this, startsRound));
		if (startsRound && !InProgress && Beatmap is not null) StartRound();
	}

	private sealed class Scope(SemaphoreSlim held) : IAsyncDisposable
	{
		private int _released;

		public ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _released, 1) == 0) held.Release();
			return ValueTask.CompletedTask;
		}
	}
}