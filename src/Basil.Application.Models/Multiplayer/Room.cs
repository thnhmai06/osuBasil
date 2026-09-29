using System.Threading.Channels;
using Basil.Application.Models.Events;
using Basil.Application.Models.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Models.Multiplayer;

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
	private GameSession? _host;

	/// <summary>The match this room is a live projection of.</summary>
	public required Match Match { get; init; }

	/// <summary>The match settings this room mutates; only the forwarding properties are public.</summary>
	public MatchSettings Settings { private get; init; } = new();

	/// <summary>Gets the runtime identifier assigned to this room.</summary>
	public required int Id
	{
		get;
		init
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	/// <summary>Gets the room's chat channel.</summary>
	public ChannelSession Channel { get; }

	/// <summary>The match's 16 slots, in order.</summary>
	public RoomSlots Slots { get; }

	public ChannelReader<RoomEvent> Events => _events.Reader;

	/// <summary>Gets or sets the name broadcast to clients.</summary>
	public string Name
	{
		get => Match.Name;
		set
		{
			if (Match.Name == value) return;
			Match.Name = value;
			Emit(new RoomNameChanged(this, value));
		}
	}

	/// <summary>Gets or sets a value that indicates whether the room is publicly visible.</summary>
	public bool IsVisible
	{
		get => Match.IsVisible;
		set
		{
			if (Match.IsVisible == value) return;
			Match.IsVisible = value;
			Emit(new RoomVisibilityChanged(this, value));
		}
	}

	/// <summary>Gets the user who created the match.</summary>
	public User? Creator => Match.Creator;

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
			Settings.Mode = value;
			Emit(new GameModeChanged(this, value));
		}
	}

	/// <summary>Gets or sets the mods applied to the whole room.</summary>
	public GameMods Mods
	{
		get => Settings.Mods;
		set
		{
			var before = Settings.Mods;
			Settings.Mods = value;
			if (Settings.Mods == before) return;
			Emit(new ModsChanged(this, value));
		}
	}

	/// <summary>Gets or sets a value that indicates whether freemod mode is enabled.</summary>
	public bool Freemods
	{
		get => Settings.Freemods;
		set
		{
			if (Settings.Freemods == value) return;
			Settings.Freemods = value;

			// Without freemod, players cannot keep mods of their own.
			if (!value)
				foreach (var slot in Slots.Where(s => s.Session is not null))
					slot.SetMods(GameMods.NoMod);
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

				foreach (var slot in Slots.Where(s => s.Session is not null))
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
				foreach (var slot in Slots.Where(s => s.Session is not null))
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

	/// <summary>Gets or sets the session currently hosting the room.</summary>
	/// <remarks>The host must be seated in this room.</remarks>
	public GameSession? Host
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

	/// <summary>Gets the round currently being played, or <see langword="null" /> when none is in progress.</summary>
	public Round? CurrentRound { get; private set; }

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
	public string UrlEmbed => $"({Match.Name})[{Url}]";

	public Room()
	{
		Channel = new ChannelSession { Channel = new RoomChannel(this) };
		Slots = new RoomSlots(this);
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
	public void Kick(GameSession player)
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
		GameSession? evicted = null;
		if (Slots.Find(player) is { Session: { } session } slot)
		{
			vacated = slot;
			evicted = session;
			Slots.Vacate(session);
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

	/// <summary>Starts a round.</summary>
	/// <param name="roundId">The id to assign to the new round.</param>
	/// <returns>The round that was started.</returns>
	/// <exception cref="InvalidOperationException">A round is already in progress, or no beatmap is selected.</exception>
	public Round Start(int roundId)
	{
		if (InProgress)
			throw new InvalidOperationException("A round is already in progress.");
		if (Beatmap is null)
			throw new InvalidOperationException("No beatmap is selected.");

		var round = new Round
		{
			Id = roundId,
			Match = Match,
			BeatmapHash = Beatmap.Hash,
			Settings = new MatchSettings
			{
				Mode = Settings.Mode,
				Mods = Settings.Mods,
				Freemods = Settings.Freemods,
				TeamType = Settings.TeamType,
				WinCondition = Settings.WinCondition,
				Seed = Settings.Seed
			},
			OccurredAt = DateTimeOffset.UtcNow,
			EndedAt = null
		};

		CurrentRound = round;

		foreach (var slot in Slots.Where(s => s.Session is not null && s.Status is not RoomSlotStatus.NoMap))
			slot.SetStatus(RoomSlotStatus.Playing);

		Emit(new RoundStarted(this, round));
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
			         s.Session is not null && s.Status is RoomSlotStatus.Playing or RoomSlotStatus.Complete))
			slot.SetStatus(RoomSlotStatus.NotReady);

		CurrentRound = null;
		Emit(new RoundAborted(this, round));
	}

	/// <summary>Closes the room, ending the match and vacating every slot.</summary>
	/// <returns>The sessions that were seated when the room closed.</returns>
	public IReadOnlyList<GameSession> Close()
	{
		var evicted = Slots.Where(s => s.Session is not null).Select(s => s.Session!).ToList();
		foreach (var session in evicted) Slots.Vacate(session);

		Match.EndedAt = DateTimeOffset.UtcNow;
		Emit(new RoomClosed(this, evicted));
		return evicted;
	}

	/// <summary>Writes an event to the room's event channel.</summary>
	/// <param name="event">The event to emit.</param>
	internal void Emit(RoomEvent @event)
	{
		_events.Writer.TryWrite(@event);
	}

	/// <summary>Clears the host without emitting a <see cref="HostChanged" /> event.</summary>
	/// <param name="host">The host to clear, or <see langword="null" />.</param>
	internal void SetHostSilently(GameSession? host)
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