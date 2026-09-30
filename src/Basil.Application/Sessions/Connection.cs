using Basil.Domain.Auth;
using Basil.Domain.Users;

namespace Basil.Application.Sessions;

/// <summary>One login of a user from one client; two connections are never equal.</summary>
public abstract class Connection(Login login)
{
	/// <summary>Gets the online session this connection belongs to.</summary>
	public UserSession Session { get; internal set; } = null!;

	/// <summary>Gets the login that opened this connection.</summary>
	public Login Login { get; } = login;

	/// <summary>Gets the user who owns this connection.</summary>
	public User User => Login.User;

	/// <summary>Gets the kind of client this connection comes from.</summary>
	public abstract ConnectionType Type { get; }

	/// <summary>Gets a value that indicates whether the connection is still open.</summary>
	public bool IsOpen { get; internal set; }
}

/// <summary>A connection from an osu! game client.</summary>
public sealed class BanchoConnection(Login login, int utcOffset) : Connection(login)
{
	/// <inheritdoc />
	public override ConnectionType Type => ConnectionType.Bancho;

	/// <summary>Gets or sets the time of the last packet received from the client.</summary>
	public DateTimeOffset LastActiveAt { get; set; } = login.Timestamp;

	/// <summary>Gets the client's UTC offset reported at login.</summary>
	public int UtcOffset { get; } = utcOffset;

	/// <summary>Gets the presence status the client last reported.</summary>
	public PlayerStatus Status { get; internal set; } = PlayerStatus.Idle;
}

/// <summary>A connection from an osu!tourney spectator client.</summary>
public sealed class TourneyConnection(Login login) : Connection(login)
{
	/// <inheritdoc />
	public override ConnectionType Type => ConnectionType.Tourney;
}

/// <summary>A connection from an IRC client.</summary>
public sealed class IrcConnection(Login login) : Connection(login)
{
	/// <inheritdoc />
	public override ConnectionType Type => ConnectionType.Irc;

	/// <summary>Gets or sets the time of the last message received from the client.</summary>
	public DateTimeOffset LastActiveAt { get; set; } = login.Timestamp;
}

/// <summary>The connection of the server's own bot.</summary>
public sealed class BotConnection(Login login) : Connection(login)
{
	/// <inheritdoc />
	public override ConnectionType Type => ConnectionType.Bot;
}