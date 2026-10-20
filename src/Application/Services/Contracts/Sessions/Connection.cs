using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Sessions
{
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
		public UserRecord User => Login.User;

		/// <summary>Gets the kind of client this connection comes from.</summary>
		public abstract ConnectionType Type { get; }

		/// <summary>Gets a value that indicates whether the connection is still open.</summary>
		public bool IsOpen { get; internal set; }

		/// <summary>Gets the time of the last message received from the client.</summary>
		public DateTimeOffset LastActiveAt { get; internal set; } = login.Timestamp;
	}
}

namespace Basil.Application.Services.Contracts.Sessions
{
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
		internal BanchoConnection(Login login, string token, int utcOffset, SessionContext context) : base(login, token)
		{
			UtcOffset = utcOffset;
			Context = context;
			SpectatorChannel = new SpectatorChannelSession(this, context.Chat);
		}

		/// <summary>The runtime dependencies used by this connection.</summary>
		internal SessionContext Context { get; }

		/// <inheritdoc />
		public override ConnectionType Type => ConnectionType.Bancho;

		/// <summary>Gets the client's UTC offset reported at login.</summary>
		public int UtcOffset { get; }

		/// <summary>Gets the presence status the client last reported.</summary>
		public PlayerStatus Status { get; internal set; } = PlayerStatus.Idle;

		/// <summary>Gets the chat channel shared with the users spectating this client.</summary>
		public SpectatorChannelSession SpectatorChannel { get; }

		/// <summary>Gets the connections spectating this client.</summary>
		public IReadOnlySet<Connection> Spectators => _spectators;

		/// <summary>Scopes this client's behaviour to a connection.</summary>
		/// <param name="by">The connection acting on this client.</param>
		/// <returns>A short-lived scope for this client and connection.</returns>
		public ScopedBanchoConnection ScopeTo(Connection by)
		{
			return new ScopedBanchoConnection(this, by);
		}

		/// <summary>
		///     Gets the connections spectating this client, for changing them; the caller holds the scope and reports the
		///     change itself.
		/// </summary>
		internal ISet<Connection> MutableSpectators => _spectators;

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
				Context.Events.Emit(new UserSpectatorLeft(this, spectator));
		}
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
}
