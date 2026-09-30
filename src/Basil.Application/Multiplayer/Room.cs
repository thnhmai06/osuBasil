using System.Threading.Channels;
using Basil.Domain.Beatmaps;
using Basil.Domain.Client;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
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

	private readonly Lobby _lobby;
	private readonly ConcurrentSet<User> _banned = [];
	private readonly ConcurrentSet<User> _referees = [];
	private readonly ConcurrentSet<TourneyConnection> _observers = [];
	private readonly Channel<RoomEvent> _events = System.Threading.Channels.Channel.CreateUnbounded<RoomEvent>();
	private readonly SemaphoreSlim _lock = new(1, 1);
	private BanchoConnection? _host;
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

	/// <summary>Gets or sets the name broadcast to clients.</summary>
	public string Name
	{
		get => Match.Value.Name;
		set
		{
			if (Match.Value.Name == value) return;
			Match.Value.Name = value;
			Emit(new RoomNameChanged(this, value));
		}
	}

	/// <summary>Gets or sets a value that indicates whether the room's match history is private.</summary>
	public bool IsPrivate
	{
		get => Match.Value.IsPrivate;
		set
		{
			if (Match.Value.IsPrivate == value) return;
			Match.Value.IsPrivate = value;
			Emit(new RoomPrivacyChanged(this, value));
		}
	}

	/// <summary>Gets the user who created the match.</summary>
	public User? Creator => Match.Value.Creator;

	/// <summary>Gets or sets the room's password.</summary>
	public string Password
	{
		private get;
		set
		{
			if (field == value) return;
			field = value;
			Emit(new RoomPasswordChanged(this, value));
		}
	} = string.Empty;

	/// <summary>Gets or sets the currently selected beatmap.</summary>
	public Beatmap? Beatmap
	{
		get;
		set
		{
			if (Equals(field, value)) return;
			field = value;
			Emit(new BeatmapChanged(this, value));
		}
	}

	/// <summary>Gets or sets the game mode played in the room.</summary>
	public GameMode Mode
	{
		get => Settings.Mode;
		set
		{
			if (Settings.Mode == value) return;
			Settings.SwitchMode(value);
			Emit(new GameModeChanged(this, value));
		}
	}

	/// <summary>Gets or sets the mods applied to the whole room.</summary>
	public GameMods Mods
	{
		get => Settings.Mods;
		set
		{
			if (Settings.Mods == value) return;
			if (Freemods && (value & ~GameMods.SpeedChangingMods) != GameMods.NoMod)
				throw new InvalidOperationException("With freemod on, the room only holds speed-changing mods.");
			Settings.Mods = value;
			Emit(new ModsChanged(this, value));
		}
	}

	/// <summary>Gets or sets a value that indicates whether freemod mode is enabled.</summary>
	/// <remarks>
	///     Turning freemod on hands the room's non-speed-changing mods to every seated player and
	///     leaves the room with only its speed-changing mods. Turning it off gives the room its
	///     speed-changing mods plus the host's own mods, and clears every player's own mods.
	/// </remarks>
	public bool Freemods
	{
		get => Settings.Freemods;
		set
		{
			if (Settings.Freemods == value) return;
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

			Emit(new FreemodsChanged(this, value));
		}
	}

	/// <summary>Gets or sets the team arrangement used for the room.</summary>
	public GameTeamType TeamType
	{
		get => Settings.TeamType;
		set
		{
			if (Settings.TeamType == value) return;
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

			Emit(new TeamTypeChanged(this, value));
		}
	}

	/// <summary>Gets or sets the condition that decides the winner of a round.</summary>
	public GameWinCondition WinCondition
	{
		get => Settings.WinCondition;
		set
		{
			if (Settings.WinCondition == value) return;
			Settings.WinCondition = value;
			Emit(new WinConditionChanged(this, value));
		}
	}

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
	/// <param name="id">The room id, carried by the client protocol.</param>
	/// <param name="match">The match the room plays.</param>
	/// <param name="settings">The room's initial settings.</param>
	/// <param name="isTournament">Whether the room is a tournament room.</param>
	internal Room(Lobby lobby, int id, Match match, MatchSettings settings, bool isTournament)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(id);
		_lobby = lobby;
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
			Emit(new PlayerLeft(this, seated, vacated, _host));
			LeaveChannel(seated);
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

		Emit(new PlayerLeft(this, by, slot, _host));
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
		Emit(new PlayerKicked(this, seated, slot, _host));
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

		RoomSlot? vacated = null;
		BanchoConnection? evicted = null;
		if (Slots.Find(player) is { Player: { } seated })
		{
			evicted = seated;
			vacated = Slots.Vacate(seated);
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

	/// <summary>Starts the next round of the match.</summary>
	/// <returns>The round that was started.</returns>
	/// <exception cref="InvalidOperationException">A round is already in progress, or no beatmap is selected.</exception>
	public Round Start()
	{
		if (InProgress) throw new InvalidOperationException("A round is already in progress.");
		if (Beatmap is null) throw new InvalidOperationException("No beatmap is selected.");

		var round = new Round
		{
			Number = (LastRound?.Number ?? 0) + 1,
			Match = Match,
			BeatmapHash = Beatmap.Hash,
			Settings = Settings.Clone(),
			StartedAt = DateTimeOffset.UtcNow,
			EndedAt = null
		};

		LastRound = round;

		foreach (var slot in Slots.Where(s => s.Player is not null && s.Status is not RoomSlotStatus.NoMap))
			slot.SetStatus(RoomSlotStatus.Playing);

		Emit(new Events.RoundStarted(this, round));
		return round;
	}

	/// <summary>Aborts the round currently in progress.</summary>
	/// <exception cref="InvalidOperationException">No round is in progress.</exception>
	public void Abort()
	{
		if (CurrentRound is not { } round)
			throw new InvalidOperationException("No round is in progress.");

		round.EndedAt = DateTimeOffset.UtcNow;
		round.Aborted = true;

		foreach (var slot in Slots.Where(s =>
			         s.Player is not null && s.Status is RoomSlotStatus.Playing or RoomSlotStatus.Complete))
			slot.SetStatus(RoomSlotStatus.NotReady);

		Emit(new Events.RoundAborted(this, round));
	}

	/// <summary>Closes the room, ending the match and vacating every slot.</summary>
	/// <returns>The connections that were seated when the room closed.</returns>
	internal IReadOnlyList<BanchoConnection> Close()
	{
		if (_closed) return [];
		if (InProgress) Abort();

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

	private void LeaveChannel(Connection connection)
	{
		if (!IsManager(connection.User)) Channel.Part(connection);
	}

	private void ReportIfEmpty()
	{
		if (!Slots.Any(slot => slot.Player is not null)) _lobby.RoomEmptied(this);
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