using System.Collections.Concurrent;
using Basil.Application.Sessions.Irc;
using Basil.Domain.Login;
using Basil.Domain.Users;

namespace Basil.Application.Sessions;

/// <summary>
///     Represents the server-side runtime identity and chat state shared by every online connection
///     for a userSession, created at login and discarded at logout. Concrete sessions are either an
///     <see cref="IrcSession" /> (a real IRC connection, chat/commands only) or a <see cref="GameSession" />
///     (a real osu! client, with full gameplay state) — the same account may hold one of each
///     simultaneously.
/// </summary>
/// <param name="id">The persistent id of the userSession.</param>
/// <param name="name">The userSession's username.</param>
/// <param name="token">The login token the session is keyed by.</param>
/// <param name="privilege">The server-side privilege flags granted at login.</param>
/// <param name="loginTime">The time at which the session was created.</param>
public abstract class UserSession(int id, string name, string token, UserPrivileges privilege, DateTimeOffset loginTime)
{
	private readonly ConcurrentDictionary<string, byte> _channels = new();

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

	/// <summary>
	///     Gets a value that indicates whether this session is the bootstrapped BanchoBot.
	/// </summary>
	/// <remarks>
	///     The bot never sends real ping packets, so its <see cref="LastRecvTime" /> never advances;
	///     GhostDisconnectService exempts it from the dead-session reap sweep for exactly that reason.
	/// </remarks>
	public bool IsBot { get; init; }

	/// <summary>
	///     Gets or sets the time at which the userSession's current silence expires, or Unix epoch when the userSession is not
	///     silenced.
	/// </summary>
	public DateTimeOffset SilenceEnd { get; set; } = DateTimeOffset.UnixEpoch;

	/// <summary>
	///     Gets or sets the away message shown to other players while this userSession is idle or null when the userSession
	///     is not away.
	/// </summary>
	public string? AwayMessage { get; set; }

	/// <summary>
	///     Gets or sets the database id of the match a referee is currently targeting from outside
	///     that match's own chat channel, set by the <c>!mp in &lt;match_id&gt;</c> command. Overrides
	///     the channel-derived match scope for every <c>!mp</c> subcommand until changed by another
	///     <c>!mp in</c>, or cleared implicitly when <c>!mp make</c> or <c>!mp makeprivate</c> points
	///     the scope at a new room.
	/// </summary>
	public int? MpScopeMatchId { get; set; }

	/// <summary>
	///     Gets the user's country, captured from the stored user record at login.
	/// </summary>
	public Country Country { get; init; } = Country.Xx;

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
	public IReadOnlyCollection<string> Channels => (IReadOnlyCollection<string>)_channels.Keys;

	/// <summary>
	///     Gets the IRC-shaped transport chat traffic is routed through for this session: a real TCP
	///     IRC connection for an <see cref="IrcSession" />, or a bancho packet bridge for a
	///     <see cref="GameSession" />.
	/// </summary>
	public abstract IIrcConnection IrcConnection { get; init; }

	/// <summary>
	///     Adds a channel name to this session's joined-channel set.
	/// </summary>
	/// <param name="name">The registry name of the channel to join.</param>
	/// <returns><see langword="true" /> if the channel was not already joined; otherwise, <see langword="false" />.</returns>
	public bool JoinChannel(string name)
	{
		return _channels.TryAdd(name, 0);
	}

	/// <summary>
	///     Removes a channel name from this session's joined-channel set.
	/// </summary>
	/// <param name="name">The registry name of the channel to leave.</param>
	/// <returns><see langword="true" /> if the channel was joined; otherwise, <see langword="false" />.</returns>
	public bool LeaveChannel(string name)
	{
		return _channels.TryRemove(name, out _);
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
}