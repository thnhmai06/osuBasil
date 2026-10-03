using Basil.Application.Sessions;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Multiplayer;

/// <summary>
///     Holds an osu! multiplayer match's live runtime state: the shared settings that are stored on
///     the underlying <see cref="Match" />, the 16 player slots, and the round lifecycle.
/// </summary>
public sealed class Room : IEquatable<Room>
{
	/// <summary>The most referees a room can have.</summary>
	public const int MaxReferees = 8;

	private readonly ConcurrentSet<User> _banned = [];
	private readonly ConcurrentSet<TourneyConnection> _observers = [];
	private readonly ConcurrentSet<User> _referees = [];

	/// <summary>Opens a room for a match.</summary>
	/// <param name="id">The room id, carried by the client protocol.</param>
	/// <param name="match">The match the room plays.</param>
	/// <param name="settings">The room's initial settings.</param>
	/// <param name="isTournament">Whether the room is a tournament room.</param>
	internal Room(int id, Match match, MatchSettings settings, bool isTournament)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(id);
		Id = id;
		Match = match;
		Settings = settings;
		IsTournament = isTournament;
		Slots = new RoomSlots(this);
		Channel = new RoomChannelSession(this);
	}

	/// <summary>The match this room is a live projection of.</summary>
	public Match Match { get; }

	/// <summary>The match settings this room mutates; only the forwarding properties are public.</summary>
	internal MatchSettings Settings { get; }

	/// <summary>Gets the runtime identifier assigned to this room.</summary>
	public int Id { get; }

	/// <summary>Gets a value that indicates whether the room is a tournament room, which stays open for a while when empty.</summary>
	public bool IsTournament { get; }

	/// <summary>Gets the room's chat channel.</summary>
	public RoomChannelSession Channel { get; }

	/// <summary>The match's 16 slots, in order.</summary>
	public RoomSlots Slots { get; }

	/// <summary>Gets the name broadcast to clients.</summary>
	public string Name => Match.Value.Name;

	/// <summary>Gets a value that indicates whether the room's match history is private.</summary>
	public bool IsPrivate => Match.Value.IsPrivate;

	/// <summary>Gets the user who created the match.</summary>
	public User? Creator => Match.Value.Creator;

	/// <summary>Gets the room's password, or an empty string for none.</summary>
	public string Password { get; internal set; } = string.Empty;

	/// <summary>Gets the currently selected beatmap.</summary>
	public BeatmapReference? Beatmap { get; internal set; }

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
	public BanchoConnection? Host { get; internal set; }

	/// <summary>Gets the most recently started round, or <see langword="null" /> before the first round.</summary>
	/// <remarks>Stays set after the round ends, until the next round starts.</remarks>
	public Round? LastRound { get; internal set; }

	/// <summary>Gets the round currently being played, or <see langword="null" /> when none is in progress.</summary>
	public Round? CurrentRound => LastRound is { EndedAt: null } round ? round : null;

	/// <summary>Gets a value that indicates whether a round is currently in progress.</summary>
	public bool InProgress => CurrentRound is not null;

	/// <summary>Gets when the running countdown ends, or <see langword="null" /> when none is running.</summary>
	public DateTimeOffset? CountdownEndsAt { get; internal set; }

	/// <summary>The running countdown, to stop when it is cancelled or replaced.</summary>
	internal IDisposable? CountdownTimer { get; set; }

	/// <summary>The timer that closes the room while it is empty.</summary>
	internal ITimer? ClosingTimer { get; set; }

	/// <summary>Whether every playing player of the current round has already been announced as loaded.</summary>
	internal bool AllLoadedAnnounced { get; set; }

	/// <summary>Whether every playing player of the current round has already been announced as having skipped the intro.</summary>
	internal bool AllSkippedAnnounced { get; set; }

	/// <summary>Gets a value that indicates whether the room has closed.</summary>
	public bool IsClosed { get; internal set; }

	/// <summary>The lock that makes the room's state transitions run one at a time.</summary>
	internal SemaphoreSlim Gate { get; } = new(1, 1);

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

	/// <inheritdoc />
	public bool Equals(Room? other)
	{
		return other is not null && Match.Equals(other.Match);
	}

	/// <summary>Makes a user a referee of the room.</summary>
	/// <param name="user">The user to add.</param>
	/// <returns><see langword="true" /> if the user was added; <see langword="false" /> if they already were a referee.</returns>
	internal bool AddReferee(User user)
	{
		return _referees.Add(user);
	}

	/// <summary>Removes a user from the room's referees.</summary>
	/// <param name="user">The user to remove.</param>
	/// <returns><see langword="true" /> if the user was removed; <see langword="false" /> if they were not a referee.</returns>
	internal bool RemoveReferee(User user)
	{
		return _referees.Remove(user);
	}

	/// <summary>Bans a user from the room.</summary>
	/// <param name="user">The user to ban.</param>
	/// <returns><see langword="true" /> if the user was banned; <see langword="false" /> if they already were.</returns>
	internal bool AddBanned(User user)
	{
		return _banned.Add(user);
	}

	/// <summary>Lifts a user's ban from the room.</summary>
	/// <param name="user">The user to unban.</param>
	/// <returns><see langword="true" /> if the ban was lifted; <see langword="false" /> if the user was not banned.</returns>
	internal bool RemoveBanned(User user)
	{
		return _banned.Remove(user);
	}

	/// <summary>Adds an osu!tourney client to the room's observers.</summary>
	/// <param name="observer">The client to add.</param>
	/// <returns><see langword="true" /> if the client was added; <see langword="false" /> if it already observed the room.</returns>
	internal bool AddObserver(TourneyConnection observer)
	{
		return _observers.Add(observer);
	}

	/// <summary>Removes an osu!tourney client from the room's observers.</summary>
	/// <param name="observer">The client to remove.</param>
	/// <returns><see langword="true" /> if the client was removed; <see langword="false" /> if it did not observe the room.</returns>
	internal bool RemoveObserver(TourneyConnection observer)
	{
		return _observers.Remove(observer);
	}

	/// <summary>Removes every observer from the room.</summary>
	internal void ClearObservers()
	{
		_observers.Clear();
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