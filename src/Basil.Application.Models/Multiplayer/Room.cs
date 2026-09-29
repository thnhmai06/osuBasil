using System.Threading.Channels;
using Basil.Domain.Beatmaps;
using Basil.Domain.Events;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Models.Multiplayer;

/// <summary>
///     Holds an osu! multiplayer match's room settings, live slot state and lifecycle flags — the
///     part of a live match that is business state rather than live-projection machinery.
/// </summary>
public sealed class Room : IEventSource<RoomEvent>
{
	private readonly ConcurrentSet<User> _banned = [];
	private readonly ConcurrentSet<User> _invited = [];
	private readonly Channel<RoomEvent> _events = System.Threading.Channels.Channel.CreateUnbounded<RoomEvent>();

	/// <summary>The match this room refers to.</summary>
	public required Match Match { get; init; }

	public Beatmap? Beatmap
	{
		get;
		set
		{
			field = value;
			_events.Writer.TryWrite(new SettingsChanged(this));
		}
	}

	/// <summary>Gets the round currently being played, or <see langword="null" /> when none is in progress.</summary>
	public Round? CurrentRound { get; set; }

	/// <summary>Gets a value that indicates whether a round is currently in progress.</summary>
	public bool InProgress => CurrentRound is not null;

	/// <summary>Gets the room's settings, including the selected beatmap, mode, and mods.</summary>
	public MatchSettings Settings { get; init; } = new();

	public readonly RoomChannel Channel;

	/// <summary>The match's 16 slots, in order.</summary>
	public readonly RoomSlots Slots;

	public ChannelReader<RoomEvent> Events => _events.Reader;

	/// <summary>
	///     Gets the runtime identifier assigned to this room, used by the osu! client to reference
	///     it while joined. This id only exists for as long as the server is running and is lost on
	///     restart; use <see cref="Match" />.<see cref="Domain.Multiplayer.Match.Id" /> to look the match up later.
	/// </summary>
	public required int Id
	{
		get;
		init
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			field = value;
		}
	}

	public string Name
	{
		get => Match.Name;
		set
		{
			Match.Name = value;
			_events.Writer.TryWrite(new SettingsChanged(this));
		}
	}

	/// <summary>Gets the room's password, which a client must supply to join.</summary>
	public string Password
	{
		private get;
		set
		{
			field = value;
			_events.Writer.TryWrite(new SettingsChanged(this));
		}
	} = string.Empty;

	public required User? Creator { get; init; }

	public User? Host
	{
		get;
		set
		{
			if (value is not null && Slots.Find(value) is null)
				throw new InvalidOperationException("The player does not have a slot in this room.");
			field = value;
		}
	}

	/// <summary>Gets the join URL of this room, in <c>osump://{id}/{password}</c> form.</summary>
	public string Url => $"osump://{Id}/{Password}";

	/// <summary>
	///     Gets an osu! chat embed for this room, formatted as a clickable name linked to <see cref="Url" />.
	/// </summary>
	public string UrlEmbed => $"({Match.Name})[{Url}]";

	public readonly ConcurrentSet<User> Referees = [];
	public IReadOnlySet<User> Banned => _banned;
	public IReadOnlySet<User> Invited => _invited;

	/// <summary>Gets a value that indicates whether <paramref name="player" /> created this match.</summary>
	/// <param name="player">The player to check.</param>
	/// <returns><see langword="true" /> if the player created this match; otherwise, <see langword="false" />.</returns>
	public bool IsCreator(User player)
	{
		return Creator is not null && Creator.Equals(player);
	}

	/// <summary>Gets a value that indicates whether <paramref name="player" /> may issue <c>!mp</c> commands on this match.</summary>
	/// <param name="player">The player to check.</param>
	/// <returns>
	///     <see langword="true" /> if the player is a referee or the creator of this match;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool IsReferee(User player)
	{
		return Referees.Contains(player) || IsCreator(player);
	}

	public void Kick(User player)
	{
		if (IsReferee(player))
			throw new InvalidOperationException("Cannot kick a referee.");

		Slots.Leave(player);
	}

	/// <summary>Bans a player from the match, removing them from the room if they are currently in it.</summary>
	/// <param name="player">The player to ban.</param>
	public void Ban(User player)
	{
		if (IsReferee(player))
			throw new InvalidOperationException("Cannot ban a referee.");

		_banned.Add(player);
		Kick(player);
	}

	/// <summary>Lifts a player's ban from the match, allowing them to join again.</summary>
	/// <param name="player">The player to unban.</param>
	/// <exception cref="InvalidOperationException"><paramref name="player" /> is not banned.</exception>
	public void Unban(User player)
	{
		if (!_banned.Remove(player)) return;
	}

	/// <summary>Grants a player referee authority for the room.</summary>
	/// <param name="referee">The player to grant referee authority to.</param>
	public void AddReferee(User referee)
	{
		Referees.Add(referee);
		_events.Writer.TryWrite(new RefereeAdded(this, referee));
	}

	/// <summary>Revokes a player's referee authority for the room.</summary>
	/// <param name="referee">The player to revoke referee authority from.</param>
	public void RemoveReferee(User referee)
	{
		Referees.Remove(referee);
		_events.Writer.TryWrite(new RefereeRemoved(this, referee));
	}

	/// <summary>Invites a player to the room.</summary>
	/// <param name="player">The player to invite.</param>
	public void Invite(User player)
	{
		_invited.Add(player);
		_events.Writer.TryWrite(new PlayerInvited(this, player));
	}

	/// <summary>Starts a round.</summary>
	public void Start()
	{
		_events.Writer.TryWrite(new RoundStarted(this));
	}

	/// <summary>Aborts the round currently in progress.</summary>
	public void Abort()
	{
		_events.Writer.TryWrite(new RoundEnded(this, true, CurrentRound));
	}

	public Room()
	{
		Channel = new RoomChannel(this);
		Slots = new RoomSlots(this);
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
}