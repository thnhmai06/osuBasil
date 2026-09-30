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
public sealed class BanchoConnection : Connection
{
	/// <summary>Initializes a game client connection.</summary>
	/// <param name="login">The login that opened the connection.</param>
	/// <param name="utcOffset">The client's UTC offset reported at login.</param>
	/// <param name="time">The clock the connection's spectator channel reads the current time from.</param>
	public BanchoConnection(Login login, int utcOffset, TimeProvider time) : base(login)
	{
		LastActiveAt = login.Timestamp;
		UtcOffset = utcOffset;
		SpectatorChannel = new SpectatorChatChannelSession(this, time);
	}

	/// <inheritdoc />
	public override ConnectionType Type => ConnectionType.Bancho;

	/// <summary>Gets or sets the time of the last packet received from the client.</summary>
	public DateTimeOffset LastActiveAt { get; set; }

	/// <summary>Gets the client's UTC offset reported at login.</summary>
	public int UtcOffset { get; }

	/// <summary>Gets the presence status the client last reported.</summary>
	public PlayerStatus Status { get; internal set; } = PlayerStatus.Idle;

	/// <summary>Gets the chat channel shared with the users spectating this client.</summary>
	public SpectatorChatChannelSession SpectatorChannel { get; }
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