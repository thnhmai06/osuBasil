using System.Collections.Concurrent;
using Basil.Application.Sessions.Irc;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Sessions;

/// <summary>
///     Represents the server-side runtime state of a single online userSession, created at login and
///     discarded at logout. Holds the outgoing packet queue, joined channels, the spectator
///     relationship, the current multiplayer match, per-mode cached stats, geolocation, and the
///     IRC-shaped chat transport bound to the session.
/// </summary>
/// <param name="id">The persistent id of the userSession.</param>
/// <param name="name">The userSession's username.</param>
/// <param name="token">The login token the session is keyed by, used to authenticate HTTP polls.</param>
/// <param name="privilege">The server-side privilege flags granted at login.</param>
/// <param name="loginTime">The time at which the session was created.</param>
public sealed class UserSession(int id, string name, string token, UserPrivileges privilege, DateTimeOffset loginTime)
{
	private readonly ConcurrentDictionary<string, byte> _channels = new();
	private readonly ConcurrentQueue<byte[]> _packetQueue = new();
	private readonly ConcurrentDictionary<int, UserSession> _spectators = new();

	/// <summary>Gets the persistent id of the userSession.</summary>
	public int Id { get; } = id;

	/// <summary>Gets the userSession's username.</summary>
	public string Name { get; } = name;

	/// <summary>Gets the login token this session is keyed by.</summary>
	public string Token { get; } = token;

	/// <summary>
	///     Gets or sets the server-side privilege flags for this session, mutable when a userSession is promoted, demoted, or
	///     restricted.
	/// </summary>
	public UserPrivileges Privilege { get; set; } = privilege;

	/// <summary>Gets the time at which this session was created.</summary>
	public DateTimeOffset LoginTime { get; } = loginTime;

	/// <summary>Gets or sets the time of the last packet or poll received from the client, used to reap dead sessions.</summary>
	public DateTimeOffset LastRecvTime { get; set; } = loginTime;

	/// <summary>Gets the client's UTC offset reported at login.</summary>
	public int UtcOffset { get; init; }

	/// <summary>
	///     Gets a value that indicates whether this session is the bootstrapped BanchoBot.
	/// </summary>
	/// <remarks>
	///     The bot never sends real ping packets, so its <see cref="LastRecvTime" /> never advances;
	///     GhostDisconnectService exempts it from the dead-session reap sweep for exactly that reason.
	/// </remarks>
	public bool IsBot { get; init; }

	/// <summary>
	///     Gets or sets a value that indicates whether this userSession accepts private messages only
	///     from friends. Set at login from the client's login body, but mutable at runtime via the
	///     TOGGLE_BLOCK_NON_FRIEND_DMS packet.
	/// </summary>
	public bool PmPrivate { get; set; }

	/// <summary>
	///     Gets or sets the time at which the userSession's current silence expires, or Unix epoch when the userSession is not
	///     silenced.
	/// </summary>
	public DateTimeOffset SilenceEnd { get; set; } = DateTimeOffset.UnixEpoch;

	/// <summary>
	///     Gets or sets the away message shown to other players while this userSession is idle, or null when the userSession
	///     is not
	///     away.
	/// </summary>
	public string? AwayMessage { get; set; }

	/// <summary>
	///     Gets or sets the presence filter preference reported by the client, controlling which users appear in its
	///     presence list.
	/// </summary>
	public PresenceFilter PresenceFilter { get; set; } = PresenceFilter.Nil;

	/// <summary>
	///     Gets or sets the database id of the match a referee is currently targeting from outside
	///     that match's own chat channel, set by the <c>!mp in &lt;match_id&gt;</c> command. Overrides
	///     the channel-derived match scope for every <c>!mp</c> subcommand until changed by another
	///     <c>!mp in</c>, or cleared implicitly when <c>!mp make</c> or <c>!mp makeprivate</c> points
	///     the scope at a new room.
	/// </summary>
	public int? MpScopeMatchId { get; set; }

	/// <summary>
	///     Gets or sets a value that indicates whether the client is currently viewing the
	///     multiplayer lobby screen, between the <see cref="Basil.Protocol.Packets.ClientPackets.JoinLobby" />
	///     and <see cref="Basil.Protocol.Packets.ClientPackets.PartLobby" /> packets.
	/// </summary>
	public bool InLobby { get; set; }

	/// <summary>
	///     Gets or sets the userSession's geolocation, captured at login and defaulting to
	///     <see cref="Country.Xx" /> with zero coordinates when the client does not report one.
	/// </summary>
	public Geolocation Geoloc { get; set; } = new(0.0, 0.0, Country.Xx);

	/// <summary>
	///     Gets or sets the hardware and client fingerprint captured at login, re-checked against
	///     score submission's own client hash to catch a submission coming from a different client
	///     session than the one currently logged in.
	/// </summary>
	public ClientDetails? Client { get; set; }

	/// <summary>
	///     Gets or sets the osu! client version captured at login, kept separate from
	///     <see cref="Client" /> because <see cref="ClientDetails" /> no longer carries a version
	///     date. Score submission's version-mismatch check compares against this.
	/// </summary>
	public OsuVersion? OsuVersion { get; set; }

	/// <summary>
	///     Gets or sets the session this userSession is currently spectating, or null when the userSession is not
	///     spectating.
	/// </summary>
	public UserSession? Spectating { get; set; }

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
	public IReadOnlyCollection<UserSession> Spectators => [.. _spectators.Values];

	/// <summary>Gets the client's currently reported presence state, including activity, selected map, mods, and mode.</summary>
	public PlayerStatus Status { get; } = new();

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

	/// <summary>
	///     Gets the case-normalized form of <see cref="Name" /> produced by
	///     <see cref="Basil.Domain.Users.User.MakeSafeName" />.
	/// </summary>
	public string SafeName => User.MakeSafeName(Name);

	/// <summary>
	///     Gets a value that indicates whether the userSession is restricted, that is, lacks the
	///     <see cref="UserPrivileges.Unrestricted" /> flag.
	/// </summary>
	public bool Restricted => (Privilege & UserPrivileges.Unrestricted) == 0;

	/// <summary>
	///     Gets the client-facing bancho protocol privileges derived from the server-side
	///     <see cref="Privilege" />, mapping the unrestricted, donator, moderator, administrator,
	///     and developer roles onto their protocol equivalents.
	/// </summary>
	public ClientPrivileges BanchoPrivilege
	{
		get
		{
			var result = (ClientPrivileges)0;
			if ((Privilege & UserPrivileges.Unrestricted) != 0) result |= ClientPrivileges.Player;
			if ((Privilege & UserPrivileges.Donator) != 0) result |= ClientPrivileges.Supporter;
			if ((Privilege & UserPrivileges.Moderator) != 0) result |= ClientPrivileges.Moderator;
			if ((Privilege & UserPrivileges.Administrator) != 0) result |= ClientPrivileges.Developer;
			if ((Privilege & UserPrivileges.Developer) != 0) result |= ClientPrivileges.Owner;
			return result;
		}
	}

	/// <summary>Gets the time remaining in the userSession's current silence, or zero when the userSession is not silenced.</summary>
	public TimeSpan RemainingSilence =>
		SilenceEnd > DateTimeOffset.UtcNow ? SilenceEnd - DateTimeOffset.UtcNow : TimeSpan.Zero;

	/// <summary>Gets a value that indicates whether the userSession is currently silenced.</summary>
	public bool Silenced => RemainingSilence != TimeSpan.Zero;

	/// <summary>Gets the set of channel names this session has joined, as a snapshot collection.</summary>
	public IReadOnlyCollection<string> Channels => [.. _channels.Keys];

	/// <summary>
	///     Gets or initializes the IRC-shaped transport chat traffic is routed through for this
	///     session: a bancho packet bridge by default, replaced with a real TCP IRC connection for a
	///     session created by an actual IRC login.
	/// </summary>
	/// <remarks>
	///     If never supplied during initialization, this lazily defaults to a bridge wrapping this
	///     session, so any <see cref="UserSession" /> works out of the box even when the
	///     constructing code never wires one explicitly (tests, mostly). A plain auto-property cannot
	///     self-reference <c>this</c> in its initializer, hence the backing field.
	/// </remarks>
	public IIrcConnection IrcConnection
	{
		get => field ??= new BanchoIrcBridgeConnection(this);
		init;
	}

	/// <summary>
	///     Adds a session to this userSession's spectator list, replacing any previous entry for the same
	///     userSession id.
	/// </summary>
	/// <param name="spectator">The session of the userSession who started spectating this userSession.</param>
	public void AddSpectator(UserSession spectator)
	{
		_spectators[spectator.Id] = spectator;
	}

	/// <summary>
	///     Removes a session from this userSession's spectator list.
	/// </summary>
	/// <param name="spectator">The session of the userSession who stopped spectating this userSession.</param>
	public void RemoveSpectator(UserSession spectator)
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
	///     Adds a channel name to this session's joined-channel set.
	/// </summary>
	/// <param name="name">The registry name of the channel to join.</param>
	public void JoinChannel(string name)
	{
		_channels[name] = 0;
	}

	/// <summary>
	///     Removes a channel name from this session's joined-channel set.
	/// </summary>
	/// <param name="name">The registry name of the channel to leave.</param>
	public void LeaveChannel(string name)
	{
		_channels.TryRemove(name, out _);
	}

	/// <summary>
	///     Gets a value that indicates whether this session has joined the named channel.
	/// </summary>
	/// <param name="name">The registry name of the channel.</param>
	/// <returns><see langword="true" /> if the channel is joined; otherwise, <see langword="false" />.</returns>
	public bool InChannel(string name)
	{
		return _channels.ContainsKey(name);
	}

	/// <summary>
	///     Drains every queued outgoing packet chunk and returns them concatenated into a single
	///     byte array, clearing the queue.
	/// </summary>
	/// <returns>The concatenated bytes of all queued packets, or an empty array when the queue is empty.</returns>
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
public sealed class PlayerStatus
{
	/// <summary>Gets or sets the activity the client is currently reporting.</summary>
	public UserActivity UserActivity { get; set; } = UserActivity.Idle;

	/// <summary>Gets or sets the free-form status text coming with the activity, shown to other players.</summary>
	public string InfoText { get; set; } = "";

	/// <summary>Gets or sets the md5 of the beatmap the userSession is currently playing or selecting.</summary>
	public string MapMd5 { get; set; } = "";

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