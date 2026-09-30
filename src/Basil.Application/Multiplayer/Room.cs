using System.Threading.Channels;
using Basil.Domain.Beatmaps;
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
	private readonly ConcurrentSet<User> _banned = [];
	private readonly ConcurrentSet<User> _invited = [];
	private readonly ConcurrentSet<User> _referees = [];
	private readonly Channel<RoomEvent> _events = System.Threading.Channels.Channel.CreateUnbounded<RoomEvent>();
	private BanchoConnection? _host;

	/// <summary>The match this room is a live projection of.</summary>
	public Match Match { get; }

	/// <summary>The match settings this room mutates; only the forwarding properties are public.</summary>
	private MatchSettings Settings { get; }

	/// <summary>Gets the runtime identifier assigned to this room.</summary>
	public int Id { get; }

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

	/// <summary>Gets or sets the connection currently hosting the room.</summary>
	/// <remarks>The host must be seated in this room.</remarks>
	public BanchoConnection? Host
	{
		get => _host;
		set
		{
			if (Equals(_host, value)) return;
			if (value is not null && Slots.Find(value) is null)
				throw new InvalidOperationException("The player does not have a slot in this room.");
			_host = value;
			Emit(new HostChanged(this, value));
		}
	}

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

	/// <summary>Gets the users invited to this private room.</summary>
	public IReadOnlySet<User> Invited => _invited;

	/// <summary>Gets the join URL of this room, in <c>osump://{id}/{password}</c> form.</summary>
	public string Url => $"osump://{Id}/{Password}";

	/// <summary>
	///     Gets an osu! chat embed for this room, formatted as a clickable name linked to <see cref="Url" />.
	/// </summary>
	public string UrlEmbed => $"({Match.Value.Name})[{Url}]";

	/// <summary>Opens a room for a match.</summary>
	/// <param name="id">The room id, carried by the client protocol.</param>
	/// <param name="match">The match the room plays.</param>
	/// <param name="settings">The room's initial settings.</param>
	public Room(int id, Match match, MatchSettings settings)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(id);
		Id = id;
		Match = match;
		Settings = settings;
		Slots = new RoomSlots(this);
		Channel = new RoomChatChannelSession(this);
	}

	/// <summary>Gets a value that indicates whether <paramref name="player" /> may issue <c>!mp</c> commands on this match.</summary>
	/// <param name="player">The player to check.</param>
	/// <returns>
	///     <see langword="true" /> if the player is a referee or the creator of this match;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool IsReferee(User player)
	{
		return _referees.Contains(player) || (Creator is not null && Creator.Equals(player));
	}

	/// <summary>
	///     Checks whether a supplied password satisfies the room's password protection.
	/// </summary>
	/// <param name="password">The password to check.</param>
	/// <returns>
	///     <see langword="true" /> if the room has no password, or <paramref name="password" />
	///     matches it; otherwise, <see langword="false" />.
	/// </returns>
	public bool VerifyPassword(string password)
	{
		return string.IsNullOrEmpty(Password) || Password == password;
	}

	/// <summary>Removes a seated player from the room.</summary>
	/// <param name="player">The player to kick.</param>
	/// <exception cref="InvalidOperationException"><paramref name="player" /> is a referee.</exception>
	public void Kick(BanchoConnection player)
	{
		if (IsReferee(player.User))
			throw new InvalidOperationException("Cannot kick a referee.");

		if (Slots.Vacate(player) is { } slot)
			Emit(new PlayerKicked(this, player, slot));
	}

	/// <summary>Bans a player from the match, removing them from the room if they are currently in it.</summary>
	/// <param name="player">The player to ban.</param>
	/// <exception cref="InvalidOperationException"><paramref name="player" /> is a referee.</exception>
	public void Ban(User player)
	{
		if (IsReferee(player))
			throw new InvalidOperationException("Cannot ban a referee.");

		if (!_banned.Add(player)) return;

		RoomSlot? vacated = null;
		BanchoConnection? evicted = null;
		if (Slots.Find(player) is { Player: { } connection } slot)
		{
			vacated = slot;
			evicted = connection;
			Slots.Vacate(connection);
		}

		Emit(new PlayerBanned(this, player, vacated, evicted));
	}

	/// <summary>Lifts a player's ban from the match, allowing them to join again.</summary>
	/// <param name="player">The player to unban.</param>
	public void Unban(User player)
	{
		if (!_banned.Remove(player)) return;
		Emit(new PlayerUnbanned(this, player));
	}

	/// <summary>Invites a player to the room.</summary>
	/// <param name="player">The player to invite.</param>
	public void Invite(User player)
	{
		_invited.Add(player);
		Emit(new PlayerInvited(this, player));
	}

	/// <summary>Seats a player in the first available slot.</summary>
	/// <param name="player">The player's game client connection.</param>
	/// <returns>The assigned slot, or <see langword="null" /> when the room is full.</returns>
	/// <exception cref="InvalidOperationException">The player is banned from the room.</exception>
	public RoomSlot? Join(BanchoConnection player)
	{
		return Slots.Seat(player);
	}

	/// <summary>Removes a player from the room, if seated.</summary>
	/// <param name="player">The player's game client connection.</param>
	public void Leave(BanchoConnection player)
	{
		if (Slots.Vacate(player) is { } slot)
			Emit(new PlayerLeft(this, player, slot));
	}

	/// <summary>Grants a player referee authority for the room.</summary>
	/// <param name="referee">The player to grant referee authority to.</param>
	public void AddReferee(User referee)
	{
		if (!_referees.Add(referee)) return;
		Emit(new RefereeAdded(this, referee));
	}

	/// <summary>Revokes a player's referee authority for the room.</summary>
	/// <param name="referee">The player to revoke referee authority from.</param>
	public void RemoveReferee(User referee)
	{
		if (!_referees.Remove(referee)) return;
		Emit(new RefereeRemoved(this, referee));
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
	public void Close()
	{
		var evicted = Slots.Where(s => s.Player is not null).Select(s => s.Player!).ToList();
		foreach (var player in evicted) Slots.Vacate(player);

		Match.Value.EndedAt = DateTimeOffset.UtcNow;
		Emit(new Events.RoomClosed(this, evicted));
		Channel.Close();
	}

	/// <summary>Writes an event to the room's event channel.</summary>
	/// <param name="event">The event to emit.</param>
	internal void Emit(RoomEvent @event)
	{
		_events.Writer.TryWrite(@event);
	}

	/// <summary>Clears the host without emitting a <see cref="HostChanged" /> event.</summary>
	/// <param name="host">The host to clear, or <see langword="null" />.</param>
	internal void SetHostSilently(BanchoConnection? host)
	{
		_host = host;
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
}