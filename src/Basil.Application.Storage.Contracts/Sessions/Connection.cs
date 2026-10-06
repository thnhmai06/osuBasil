using Basil.Domain.Auth;
using Basil.Domain.Users;

namespace Basil.Application.Storage.Contracts.Sessions;

/// <summary>One login of a user from one client; two connections are never equal.</summary>
public abstract class Connection(Login login, string token)
{
	/// <summary>Gets the online session this connection belongs to.</summary>
	public UserSession Session { get; internal set; } = null!;

	/// <summary>Gets the login that opened this connection.</summary>
	public Login Login { get; } = login;

	/// <summary>Gets the opaque token the client presents to be recognised as this connection.</summary>
	public string Token { get; } = token;

	/// <summary>Gets the user who owns this connection.</summary>
	public User User => Login.User;

	/// <summary>Gets the kind of client this connection comes from.</summary>
	public abstract ConnectionType Type { get; }

	/// <summary>Gets a value that indicates whether the connection is still open.</summary>
	public bool IsOpen { get; internal set; }

	/// <summary>Gets the time of the last message received from the client.</summary>
	public DateTimeOffset LastActiveAt { get; internal set; } = login.Timestamp;
}

/// <summary>A connection from an osu! game client.</summary>
public sealed class BanchoConnection : Connection
{
	/// <summary>Initializes a game client connection.</summary>
	/// <param name="login">The login that opened the connection.</param>
	/// <param name="token">The token that identifies the connection.</param>
	/// <param name="utcOffset">The client's UTC offset reported at login.</param>
	internal BanchoConnection(Login login, string token, int utcOffset) : base(login, token)
	{
		UtcOffset = utcOffset;
		SpectatorChannel = new SpectatorChannelSession(this);
	}

	/// <inheritdoc />
	public override ConnectionType Type => ConnectionType.Bancho;

	/// <summary>Gets the client's UTC offset reported at login.</summary>
	public int UtcOffset { get; }

	/// <summary>Gets the presence status the client last reported.</summary>
	public PlayerStatus Status { get; internal set; } = PlayerStatus.Idle;

	/// <summary>Gets the chat channel shared with the users spectating this client.</summary>
	public SpectatorChannelSession SpectatorChannel { get; }
}

/// <summary>A connection from an osu!tourney spectator client.</summary>
public sealed class TourneyConnection : Connection
{
	/// <summary>Initializes the connection a login opened.</summary>
	/// <param name="login">The login that opened the connection.</param>
	/// <param name="token">The token that identifies the connection.</param>
	internal TourneyConnection(Login login, string token) : base(login, token)
	{
	}

	/// <inheritdoc />
	public override ConnectionType Type => ConnectionType.Tourney;
}

/// <summary>A connection from an IRC client.</summary>
public sealed class IrcConnection : Connection
{
	/// <summary>Initializes the connection a login opened.</summary>
	/// <param name="login">The login that opened the connection.</param>
	/// <param name="token">The token that identifies the connection.</param>
	internal IrcConnection(Login login, string token) : base(login, token)
	{
	}

	/// <inheritdoc />
	public override ConnectionType Type => ConnectionType.Irc;
}

/// <summary>A connection from a client of the HTTP API.</summary>
public sealed class ApiConnection : Connection
{
	/// <summary>Initializes the connection a login opened.</summary>
	/// <param name="login">The login that opened the connection.</param>
	/// <param name="token">The token that identifies the connection.</param>
	internal ApiConnection(Login login, string token) : base(login, token)
	{
	}

	/// <inheritdoc />
	public override ConnectionType Type => ConnectionType.Api;
}

/// <summary>A connection through which another account acts for this connection's user, within a scope.</summary>
/// <remarks>
///     It carries the user's identity and session, so operations are checked against and attributed to that user,
///     but only the permissions in <see cref="Scope" /> count. It is never registered, never joins a channel and never
///     closes.
/// </remarks>
public sealed class DelegatedConnection : Connection
{
	/// <summary>Initializes a connection that acts for a user through one of their open connections.</summary>
	/// <param name="target">The user's open connection.</param>
	/// <param name="by">The connection of the account acting for the user.</param>
	/// <param name="scope">The permissions the acting account may use in the user's name.</param>
	internal DelegatedConnection(Connection target, Connection by, Permissions scope) : base(target.Login, by.Token)
	{
		Session = target.Session;
		IsOpen = true;
		Delegate = by;
		Scope = scope;
	}

	/// <summary>Gets the connection of the account acting for the user.</summary>
	public Connection Delegate { get; }

	/// <summary>Gets the permissions the acting account may use in the user's name.</summary>
	public Permissions Scope { get; }

	/// <inheritdoc />
	public override ConnectionType Type => Delegate.Type;
}

/// <summary>The kinds of client a user can connect with.</summary>
public enum ConnectionType : byte
{
	/// <summary>An osu! game client.</summary>
	Bancho,

	/// <summary>An osu!tourney spectator client.</summary>
	Tourney,

	/// <summary>An IRC client.</summary>
	Irc,

	/// <summary>A client of the HTTP API.</summary>
	Api
}