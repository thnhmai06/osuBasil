using System.Collections.Concurrent;
using Basil.Application.Sessions.Irc;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Sessions;

/// <summary>
///     Represents a real osu! client logged in over the bancho binary protocol: outgoing packet
///     queue, joined channels, spectator relationships, current multiplayer match, and per-mode
///     cached stats. BasilBot is also represented as a <see cref="GameSession" /> — a server-owned
///     synthetic session with no real <see cref="Client" />, needed because <see cref="Spectating" />
///     lets it watch every online userSession and expose their input over SSE.
/// </summary>
/// <param name="id">The persistent id of the userSession.</param>
/// <param name="name">The userSession's username.</param>
/// <param name="token">The login token the session is keyed by, used to authenticate HTTP polls.</param>
/// <param name="privilege">The server-side privilege flags granted at login.</param>
/// <param name="loginTime">The time at which the session was created.</param>
public sealed class GameSession(int id, string name, string token, UserPrivileges privilege, DateTimeOffset loginTime)
	: UserSession(id, name, token, privilege, loginTime)
{
	private readonly ConcurrentQueue<byte[]> _packetQueue = new();
	private readonly ConcurrentDictionary<int, GameSession> _spectators = new();

	/// <summary>Gets the client's UTC offset reported at login.</summary>
	public int UtcOffset { get; init; }

	/// <summary>
	///     Gets or sets a value that indicates whether this userSession accepts private messages only
	///     from friends. Set at login from the client's login body, but mutable at runtime via the
	///     TOGGLE_BLOCK_NON_FRIEND_DMS packet.
	/// </summary>
	public bool PmPrivate { get; set; }

	/// <summary>
	///     Gets or sets the presence filter preference reported by the client, controlling which users appear in its
	///     presence list.
	/// </summary>
	public PresenceFilter PresenceFilter { get; set; } = PresenceFilter.Nil;

	/// <summary>
	///     Gets or sets a value that indicates whether the client is currently viewing the
	///     multiplayer lobby screen, between the <see cref="Basil.Protocol.Packets.ClientPackets.JoinLobby" />
	///     and <see cref="Basil.Protocol.Packets.ClientPackets.PartLobby" /> packets.
	/// </summary>
	// ReSharper disable once UnusedAutoPropertyAccessor.Global
	public bool InLobby { get; set; }

	/// <summary>
	///     Gets or sets the hardware and client fingerprint captured at login, re-checked against
	///     score submission's own client hash to catch a submission coming from a different client
	///     session than the one currently logged in.
	/// </summary>
	public ClientDetails? Client { get; init; }

	/// <summary>
	///     Gets or sets the osu! client version captured at login, kept separate from
	///     <see cref="Client" /> because <see cref="ClientDetails" /> no longer carries a version
	///     date. Score submission's version-mismatch check compares against this.
	/// </summary>
	public OsuVersion? OsuVersion { get; init; }

	/// <summary>
	///     Gets or sets the session this userSession is currently spectating, or null when the userSession is not
	///     spectating.
	/// </summary>
	public GameSession? Spectating { get; set; }

	/// <summary>
	///     Gets or sets the multiplayer match this userSession is currently in, or null when the userSession is not in a
	///     match.
	/// </summary>
	public MatchSession? Match { get; set; }

	/// <summary>
	///     Gets or sets a value that indicates whether this userSession is spectating someone without the
	///     target being informed. Toggled by the <c>!stealth</c> command and off by default.
	/// </summary>
	public bool Stealth { get; set; }

	/// <summary>Gets the sessions currently spectating this userSession, as a snapshot collection.</summary>
	public IReadOnlyCollection<GameSession> Spectators => (IReadOnlyCollection<GameSession>)_spectators.Values;

	/// <summary>Gets the client's currently reported presence state, including activity, selected map, mods, and mode.</summary>
	public UserStatus Status { get; } = new();

	/// <summary>
	///     Gets the per-mode stats for this userSession, loaded into memory at login and never re-queried
	///     per packet.
	/// </summary>
	public Dictionary<GameMode, CachedPlayerStats> ModeStats { get; } = new();

	/// <summary>
	///     Gets the cached stats for the userSession's currently selected mode, or null when that mode has no cached
	///     entry.
	/// </summary>
	public CachedPlayerStats? CurrentStats => ModeStats.GetValueOrDefault(Status.Mode);

	/// <inheritdoc />
	/// <remarks>
	///     Lazily defaults to a bridge wrapping this session that re-encodes IRC-shaped chat into
	///     bancho packets, so any <see cref="GameSession" /> works out of the box even when the
	///     constructing code never wires one explicitly (tests, mostly).
	/// </remarks>
	public override IIrcConnection IrcConnection => field ??= new BanchoIrcBridgeConnection(this);

	/// <summary>
	///     Adds a session to this userSession's spectator list, replacing any previous entry for the same
	///     userSession id.
	/// </summary>
	/// <param name="spectator">The session of the userSession who started spectating this userSession.</param>
	public void AddSpectator(GameSession spectator)
	{
		_spectators[spectator.Id] = spectator;
	}

	/// <summary>
	///     Removes a session from this userSession's spectator list.
	/// </summary>
	/// <param name="spectator">The session of the userSession who stopped spectating this userSession.</param>
	public void RemoveSpectator(GameSession spectator)
	{
		_spectators.TryRemove(spectator.Id, out _);
	}

	/// <summary>
	///     Appends a chunk of raw bancho packet bytes to this session's outgoing queue, delivered to
	///     the client on its next HTTP poll.
	/// </summary>
	/// <param name="data">The raw packet bytes to send.</param>
	public void Enqueue(byte[] data)
	{
		_packetQueue.Enqueue(data);
	}

	/// <summary>
	///     Drains every queued outgoing packet chunk and returns them concatenated into a single
	///     byte array, clearing the queue.
	/// </summary>
	/// <returns>The concatenated bytes of all queued packets or an empty array when the queue is empty.</returns>
	public byte[] Dequeue()
	{
		using var buffer = new MemoryStream();
		while (_packetQueue.TryDequeue(out var chunk))
			buffer.Write(chunk, 0, chunk.Length);

		return buffer.ToArray();
	}
}

/// <summary>
///     Represents the client's currently reported presence state, updated as the userSession idles,
///     selects a map, changes mods, or switches modes.
/// </summary>
public sealed class UserStatus
{
	/// <summary>Gets or sets the activity the client is currently reporting.</summary>
	public UserActivity UserActivity { get; set; } = UserActivity.Idle;

	/// <summary>Gets or sets the free-form status text coming with the activity, shown to other players.</summary>
	public string InfoText { get; set; } = string.Empty;

	/// <summary>Gets or sets the md5 of the beatmap the userSession is currently playing or selecting.</summary>
	public string MapMd5 { get; set; } = string.Empty;

	/// <summary>Gets or sets the mods the userSession currently has active.</summary>
	public Mods Mods { get; set; } = Mods.NoMod;

	/// <summary>Gets or sets the game mode the userSession currently has selected.</summary>
	public GameMode Mode { get; set; } = GameMode.Standard;

	/// <summary>Gets or sets the id of the beatmap the userSession is currently playing or selecting.</summary>
	public int MapId { get; set; }
}

/// <summary>
///     Represents a userSession's cached stats for a single game mode, loaded at login and never
///     re-queried per packet.
/// </summary>
/// <param name="TotalScore">The userSession's lifetime total score in the mode.</param>
/// <param name="RankedScore">The userSession's lifetime ranked score in the mode.</param>
/// <param name="Plays">The number of plays the userSession has in the mode.</param>
/// <param name="Rank">The userSession's rank in the mode.</param>
public sealed record CachedPlayerStats(long TotalScore, long RankedScore, int Plays, int Rank);
