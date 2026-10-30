using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Users.Connections;

/// <summary>One login of a user from one client; two connections are never equal.</summary>
public abstract class Connection(Session login, string token) : IActor
{
	/// <summary>Gets the online user this connection belongs to.</summary>
	public User User { get; internal set; } = null!;

	/// <summary>Gets the login that opened this connection.</summary>
	public Session Session { get; } = login;

	/// <summary>Gets the opaque token that identifies this connection.</summary>
	public string Token { get; } = token;

	/// <summary>Gets the user who owns this connection.</summary>
	public UserRecord Record => Session.User;

	/// <inheritdoc />
	public virtual UserPermissions EffectivePermissions(DateTimeOffset now)
	{
		return User.EffectivePermissions(now);
	}

	/// <summary>Tells whether this connection's authority is above an account's permissions.</summary>
	/// <param name="granted">The permissions granted to this connection's user when the scope was made.</param>
	/// <param name="target">The permissions granted to the account acted on.</param>
	/// <returns><see langword="true" /> when the granted permissions strictly include the target's.</returns>
	internal virtual bool Outranks(UserPermissions granted, UserPermissions target)
	{
		return granted.Allows(target) && granted != target;
	}

	/// <summary>Gets the kind of connection.</summary>
	public abstract SessionType Type { get; }

	/// <summary>Gets a value that indicates whether the connection is still open.</summary>
	public bool IsOpen { get; internal set; }

	/// <summary>Gets when this connection was last active.</summary>
	public DateTimeOffset LastActiveAt
	{
		get => Session.LastActiveAt;
		internal set => Session.MarkActive(value);
	}
}

/// <summary>A connection from an osu! game client.</summary>
public sealed class BanchoConnection : Connection
{
	private readonly Lock _spectating = new();
	private readonly ConcurrentSet<Connection> _spectators = [];

	/// <summary>Initializes a game client connection.</summary>
	/// <param name="login">The login that opened the connection.</param>
	/// <param name="token">The token that identifies the connection.</param>
	/// <param name="utcOffset">The client's UTC offset reported at login.</param>
	/// <param name="context">The runtime dependencies for the connection's behaviour.</param>
	internal BanchoConnection(Session login, string token, int utcOffset, ConnectionContext context) : base(login, token)
	{
		UtcOffset = utcOffset;
		Context = context;
	}

	/// <summary>The runtime dependencies used by this connection.</summary>
	internal ConnectionContext Context { get; }

	/// <inheritdoc />
	public override SessionType Type => SessionType.Osu;

	/// <summary>Gets the client's UTC offset reported at login.</summary>
	public int UtcOffset { get; }

	/// <summary>Gets the presence status the client last reported.</summary>
	public PlayerStatus Status { get; internal set; } = PlayerStatus.Idle;

	/// <summary>Gets the connections spectating this client.</summary>
	public IReadOnlySet<Connection> Spectators => _spectators;

	/// <summary>Gets a value that indicates whether an HTTP API connection is spectating this client.</summary>
	/// <remarks>The host decodes the client's frames only while this is <see langword="true" />.</remarks>
	public bool HasApiSpectators => _spectators.Any(spectator => spectator is ApiConnection);

	/// <summary>
	///     Gets the connections spectating this client, for changing them; the caller holds the scope and reports the
	///     change itself.
	/// </summary>
	internal ISet<Connection> MutableSpectators => _spectators;

	/// <summary>Scopes this client's behaviour to a connection.</summary>
	/// <param name="by">The connection acting on this client.</param>
	/// <returns>A short-lived scope for this client and connection.</returns>
	public ScopedBanchoConnection ScopeTo(Connection by)
	{
		return new ScopedBanchoConnection(this, by);
	}

	/// <summary>Enters the scope in which the client's spectators change one at a time.</summary>
	/// <returns>A scope that must be disposed when the change is complete.</returns>
	internal Lock.Scope EnterSpectators()
	{
		return _spectating.EnterScope();
	}

	/// <summary>Removes every spectator when this client goes away and announces each one.</summary>
	internal void ReleaseSpectators()
	{
		using var scope = EnterSpectators();
		var spectators = Spectators.ToArray();
		MutableSpectators.Clear();
		foreach (var spectator in spectators)
			Context.Events.Publish(new UserSpectatorLeft(this, spectator));
	}
}

/// <summary>A connection from an osu!tourney spectator client.</summary>
public sealed class TourneyConnection : Connection
{
	/// <summary>Initializes the connection a login opened.</summary>
	/// <param name="login">The login that opened the connection.</param>
	/// <param name="token">The token that identifies the connection.</param>
	internal TourneyConnection(Session login, string token) : base(login, token)
	{
	}

	/// <inheritdoc />
	public override SessionType Type => SessionType.OsuTourney;
}

/// <summary>A connection from an IRC client.</summary>
public sealed class IrcConnection : Connection
{
	/// <summary>Initializes the connection a login opened.</summary>
	/// <param name="login">The login that opened the connection.</param>
	/// <param name="token">The token that identifies the connection.</param>
	internal IrcConnection(Session login, string token) : base(login, token)
	{
	}

	/// <inheritdoc />
	public override SessionType Type => SessionType.Irc;
}

/// <summary>A connection from a client of the HTTP API.</summary>
public sealed class ApiConnection : Connection
{
	/// <summary>Initializes the connection a login opened.</summary>
	/// <param name="login">The login that opened the connection.</param>
	/// <param name="token">The token that identifies the connection.</param>
	/// <param name="tokenLifetime">How long the token stays valid after each use.</param>
	internal ApiConnection(Session login, string token, TimeSpan tokenLifetime) : base(login, token)
	{
		TokenLifetime = tokenLifetime;
	}

	/// <summary>Gets how long the token stays valid after each use.</summary>
	public TimeSpan TokenLifetime { get; }

	/// <inheritdoc />
	public override SessionType Type => SessionType.Api;
}

/// <summary>The server's own connection for BasilBot; no client is behind it.</summary>
public sealed class BotConnection : Connection
{
	/// <summary>Initializes the server's connection for BasilBot.</summary>
	/// <param name="login">The login that opened the connection.</param>
	/// <param name="token">The token that identifies the connection.</param>
	internal BotConnection(Session login, string token) : base(login, token)
	{
	}

	/// <inheritdoc />
	public override SessionType Type => SessionType.Bot;
}

/// <summary>
///     The operator's connection: BasilBot acting for whoever sits at the server's console. It is the one authority
///     that skips the rights checks, because the operator owns the machine; it is never registered, so no client can
///     present it and BasilBot's own connection gains nothing from it.
/// </summary>
public sealed class OperatorConnection : Connection
{
	/// <summary>Initializes the operator's connection.</summary>
	/// <param name="login">The login that stands for the console.</param>
	/// <param name="token">The token that identifies the connection.</param>
	internal OperatorConnection(Session login, string token) : base(login, token)
	{
	}

	/// <inheritdoc />
	public override SessionType Type => SessionType.Bot;

	/// <inheritdoc />
	public override UserPermissions EffectivePermissions(DateTimeOffset now)
	{
		return UserPermissionsGroups.All;
	}

	/// <inheritdoc />
	internal override bool Outranks(UserPermissions granted, UserPermissions target)
	{
		return true;
	}
}
