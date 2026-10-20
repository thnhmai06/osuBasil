using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Basil.Application.Objects.Anticheat;
using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer.Rooms;
using Basil.Application.Objects.Scores;
using Basil.Application.Objects.Sessions;
using Basil.Application.Storage.Auth;
using Basil.Application.Storage.Users;
using Basil.Domain.Auth;
using Basil.Domain.Bancho;
using Basil.Domain.Common;
using Basil.Domain.Permissions;
using Basil.Domain.Users;
using Basil.Application.Services.Users;
using Basil.Application.Services.Scores;

namespace Basil.Application.Objects.Users;

/// <summary>The users who are online and the connections through which they act.</summary>
public sealed class UserRegistry : IUserConnectionsPort, IRoomUsersPort
{
	private readonly ConcurrentDictionary<string, Connection> _connections = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<UserRecord, User> _online = new();
	private readonly Lock _sync = new(); // ponytail: one lock for all logins; per-user locks if login rate ever matters
	private readonly AccountService _accounts;
	private readonly IUserRepository _users;
	private readonly ILoginRepository _logins;
	private readonly TimeProvider _clock;
	private readonly IEventPublisher<UserEvent> _events;
	private readonly UserContext _context;
	private readonly SessionContext _session;

	/// <summary>How long a connection must be idle before a new login of the same kind replaces it.</summary>
	internal static readonly TimeSpan ReplaceAfterIdle = TimeSpan.FromSeconds(10);

	/// <summary>A logout an osu! client sends this soon after login is ignored.</summary>
	internal static readonly TimeSpan IgnoreLogoutWithin = TimeSpan.FromSeconds(1);

	/// <summary>Creates the registry that owns online users and their connections.</summary>
	/// <param name="accounts">The account operations that use persistence.</param>
	/// <param name="users">The stored accounts.</param>
	/// <param name="restrictions">The stored restrictions.</param>
	/// <param name="moderationLog">The recorded moderation history.</param>
	/// <param name="relationships">The relationships users set toward each other.</param>
	/// <param name="logins">The recorded logins.</param>
	/// <param name="credentials">The stored passwords.</param>
	/// <param name="clock">The clock for connection and permission times.</param>
	/// <param name="events">The publisher for user events.</param>
	/// <param name="chat">The runtime dependencies for user channels.</param>
	/// <param name="lobby">The deferred lookup for rooms containing game clients.</param>
	/// <param name="scores">The score validation and persistence operations.</param>
	/// <param name="scoreEvents">The publisher for score events.</param>
	/// <param name="anticheatEvents">The publisher for anticheat events.</param>
	internal UserRegistry(AccountService accounts, IUserRepository users, IRestrictionRepository restrictions,
		IModerationLogRepository moderationLog, IRelationshipRepository relationships, ILoginRepository logins,
		ICredentialRepository credentials, TimeProvider clock, IEventPublisher<UserEvent> events, ChannelContext chat,
		Func<IConnectionLobbyPort> lobby, ScoreRecorder scores, IEventPublisher<ScoreEvent> scoreEvents,
		IEventPublisher<AnticheatEvent> anticheatEvents)
	{
		_accounts = accounts;
		_users = users;
		_logins = logins;
		_clock = clock;
		_events = events;
		_context = new UserContext(clock, events, this, users, restrictions, moderationLog, relationships, logins,
			credentials, this, chat);
		_session = new SessionContext(clock, events, this, relationships, chat,
			new DeferredConnectionLobbyPort(lobby), scores, scoreEvents, anticheatEvents);
	}

	internal SessionContext Session => _session;

	/// <summary>Gets every online user by its account.</summary>
	public IReadOnlyDictionary<UserRecord, User> Online => _online;

	/// <summary>Finds the open connection a token identifies.</summary>
	/// <param name="token">The token the client presented.</param>
	/// <returns>The connection, or <see langword="null" /> when no open connection has that token.</returns>
	public Connection? Find(string token)
	{
		return _connections.GetValueOrDefault(token);
	}

	/// <summary>Finds the osu! client a connection is spectating.</summary>
	/// <param name="connection">The connection to look up.</param>
	/// <returns>The online osu! client being spectated, or <see langword="null" /> when the connection is not spectating.</returns>
	public BanchoConnection? FindSpectating(Connection connection)
	{
		// ponytail: scans every online osu! client; add an index if the online count grows large.
		return _online.Values
			.Select(user => user.Bancho)
			.FirstOrDefault(host => host is not null && host.Spectators.Contains(connection));
	}

	/// <summary>Gets the online account object, or creates an unregistered object for an offline account.</summary>
	/// <param name="record">The account to get.</param>
	/// <returns>The live online object when the account is online; otherwise a transient offline object.</returns>
	public User Of(UserRecord record)
	{
		return _online.GetValueOrDefault(record) ?? new User(record, _context);
	}

	/// <summary>Scopes account creation to a connection and its authority.</summary>
	/// <param name="by">The connection acting.</param>
	/// <returns>A short-lived scope for creating accounts.</returns>
	public ScopedUserRegistry ScopeTo(Connection by)
	{
		return new ScopedUserRegistry(this, by);
	}

	/// <summary>Authenticates a login attempt and, on success, opens a connection of the requested kind.</summary>
	/// <param name="attempt">The claimed username and password.</param>
	/// <param name="type">The kind of connection requested.</param>
	/// <param name="ip">The IP address the login came from.</param>
	/// <param name="client">The osu! client that logged in, or <see langword="null" /> for IRC and the HTTP API.</param>
	/// <param name="utcOffset">The client's UTC offset reported at login; used only by osu! game clients.</param>
	/// <param name="cancellationToken">A token that cancels the login.</param>
	/// <returns>The login's outcome: the new connection on success, or the reason it failed.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="type" /> is not a defined value.</exception>
	/// <exception cref="ArgumentException"><paramref name="type" /> is BasilBot's own connection.</exception>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="client" /> is <see langword="null" /> for an osu! or osu!tourney login.
	/// </exception>
	/// <remarks>An osu!tourney login needs <see cref="Permissions.RoomObserve" /> in effect. Successful logins are recorded.</remarks>
	public async Task<LoginResult> LoginAsync(LoginAttempt attempt, ConnectionType type, IPAddress ip,
		BanchoClient? client, int utcOffset, CancellationToken cancellationToken = default)
	{
		type.ThrowIfUndefined();
		switch (type)
		{
			case ConnectionType.Bot:
				throw new ArgumentException("The bot's connection cannot be requested.", nameof(type));
			case ConnectionType.Bancho or ConnectionType.Tourney when client is null:
				throw new ArgumentNullException(nameof(client), "An osu! client login must report its client.");
		}

		var (record, failure) = await _accounts.FindLoginUserAsync(attempt, cancellationToken);
		if (failure is { } loginFailure) return LoginResult.Fail(loginFailure);
		if (record is null) return LoginResult.Fail(LoginFailure.UnknownUser);

		var now = _clock.GetUtcNow();
		var restrictions = await _accounts.ListRunningRestrictionsAsync(record, now, cancellationToken);
		var login = new Login { User = record, Ip = ip, Client = client, Timestamp = now };
		var token = NewToken();
		Connection connection = type switch
		{
			ConnectionType.Bancho => new BanchoConnection(login, token, utcOffset, _session),
			ConnectionType.Tourney => new TourneyConnection(login, token),
			ConnectionType.Irc => new IrcConnection(login, token),
			_ => new ApiConnection(login, token)
		};

		var openFailure = Open(connection, restrictions);
		if (openFailure is not null) return LoginResult.Fail(openFailure.Value);

		await _logins.RecordAsync(login, cancellationToken);
		return LoginResult.Success(connection);
	}

	/// <summary>Checks a registration attempt without creating the account.</summary>
	/// <param name="attempt">The registration attempt to check.</param>
	/// <param name="cancellationToken">A token that cancels the check.</param>
	/// <returns><see langword="null" /> when the attempt would succeed; otherwise, why it would fail.</returns>
	/// <remarks>Registration is refused while account creation is locked.</remarks>
	public Task<RegistrationFailure?> CheckRegistrationAsync(RegisterAttempt attempt,
		CancellationToken cancellationToken = default)
	{
		return _accounts.CheckRegistrationAsync(attempt, cancellationToken);
	}

	/// <summary>Creates the account a registration attempt asks for.</summary>
	/// <param name="attempt">The registration attempt.</param>
	/// <param name="cancellationToken">A token that cancels the registration.</param>
	/// <returns>The new user, or why the registration failed.</returns>
	/// <remarks>Registration is refused while account creation is locked.</remarks>
	public async Task<(UserRecord? User, RegistrationFailure? Failure)> RegisterAsync(RegisterAttempt attempt,
		CancellationToken cancellationToken = default)
	{
		var failure = await CheckRegistrationAsync(attempt, cancellationToken);
		if (failure is not null) return (null, failure);

		return await _accounts.CreateAsync(new UserData { Name = attempt.Username }, attempt.PasswordHash,
			cancellationToken);
	}

	/// <summary>Closes a connection, taking its user offline if it was their last one.</summary>
	/// <param name="connection">The connection to close.</param>
	/// <param name="reason">Why the connection is closed.</param>
	/// <remarks>
	///     An osu! client's logout within one second of login is ignored. Closing an already closed connection does nothing.
	/// </remarks>
	public void Close(Connection connection, ConnectionCloseReason reason)
	{
		if (reason is ConnectionCloseReason.LoggedOut && connection is BanchoConnection &&
		    _clock.GetUtcNow() - connection.Login.Timestamp < IgnoreLogoutWithin)
			return;

		using var scope = Enter();
		CloseCore(connection, reason);
	}

	/// <summary>Records that a client just sent something.</summary>
	/// <param name="connection">The connection that sent a message.</param>
	public void MarkActive(Connection connection)
	{
		if (connection.IsOpen)
			connection.LastActiveAt = _clock.GetUtcNow();
	}

	/// <summary>Closes every connection that has sent nothing for too long.</summary>
	/// <returns>How many connections were closed.</returns>
	/// <remarks>The bot connection is not closed by this operation.</remarks>
	public int CloseIdle()
	{
		var now = _clock.GetUtcNow();
		var idleConnections = _online.Values
			.SelectMany(user => user.Connections)
			.Where(connection => connection is { IsOpen: true, Type: not ConnectionType.Bot } &&
			                     now - connection.LastActiveAt > connection.Type.IdleTimeout())
			.ToList();

		foreach (var connection in idleConnections)
			Close(connection, ConnectionCloseReason.TimedOut);

		return idleConnections.Count;
	}

	/// <summary>Brings BasilBot online with a connection of its own.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The existing connection when BasilBot is already online, or <see langword="null" /> when its record is not
	///     stored or its connection could not be opened.
	/// </returns>
	/// <remarks>Used by the server at startup.</remarks>
	public async Task<BotConnection?> ConnectBotAsync(CancellationToken cancellationToken = default)
	{
		var user = await _users.GetAsync(BasilBot.Id, cancellationToken);
		if (user is null) return null;

		var online = _online.GetValueOrDefault(user);
		if (online?.Bot is { IsOpen: true } connection) return connection;

		var login = new Login { User = user, Ip = IPAddress.Loopback, Timestamp = _clock.GetUtcNow() };
		var bot = new BotConnection(login, NewToken());
		return Open(bot, []) is null ? bot : null;
	}

	/// <summary>Opens an authenticated connection, bringing its user online if this is their first.</summary>
	/// <param name="connection">The new connection.</param>
	/// <param name="restrictions">The user's restrictions that have not ended.</param>
	/// <returns><see langword="null" /> on success; otherwise, why the connection was refused.</returns>
	/// <remarks>
	///     A kind that allows one connection per user replaces a connection of the same kind that has been
	///     idle for at least <see cref="ReplaceAfterIdle" />, and refuses the new one otherwise. An osu!tourney
	///     connection needs <see cref="Permissions.RoomObserve" /> in effect.
	/// </remarks>
	internal LoginFailure? Open(Connection connection, IReadOnlyList<Restriction> restrictions)
	{
		using var scope = Enter();

		// The online user holds the current record; a login may have read an older one.
		var record = _online.GetValueOrDefault(connection.Record)?.Record ?? connection.Record;
		if (connection.Type is ConnectionType.Tourney &&
		    !record.Value.Permissions.Effective(restrictions, _clock.GetUtcNow())
			    .Allows(Permissions.RoomObserve))
			return LoginFailure.NoTourneyPermission;

		var user = _online.GetValueOrDefault(connection.Record);

		if (user is not null && !connection.Type.AllowsMany() &&
		    user[connection.Type].FirstOrDefault() is { } old)
		{
			if (_clock.GetUtcNow() - old.LastActiveAt < ReplaceAfterIdle)
				return LoginFailure.AlreadyOnline;

			CloseCore(old, ConnectionCloseReason.Replaced);
			user = _online.GetValueOrDefault(connection.Record);
		}

		var cameOnline = user is null;
		user ??= Of(connection.Record);
		if (cameOnline) Add(user);

		user.ChangeRestrictions(_ => [.. restrictions]);
		connection.User = user;
		user.Add(connection);
		if (connection.Type is not ConnectionType.Tourney)
			user.PmChannel.ScopeTo(connection).Join();
		connection.IsOpen = true;

		// The bot's token is never findable: nobody can present it, and nobody should be able to try.
		if (connection.Type is not ConnectionType.Bot) Index(connection);

		_events.Publish(new UserConnectionOpened(connection, cameOnline));
		return null;
	}

	/// <summary>Creates the token of a new connection.</summary>
	/// <returns>32 random bytes, encoded for use in URLs and headers.</returns>
	internal static string NewToken()
	{
		return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
	}

	/// <summary>Closes every open connection of a user.</summary>
	/// <param name="user">The user whose connections are closed.</param>
	/// <param name="reason">Why they are closed.</param>
	internal void CloseAll(UserRecord user, ConnectionCloseReason reason)
	{
		var online = _online.GetValueOrDefault(user);
		if (online is null) return;

		foreach (var connection in online.Connections.Where(connection => connection.IsOpen).ToList())
			Close(connection, reason);
	}

	void IUserConnectionsPort.CloseAll(UserRecord user, ConnectionCloseReason reason)
	{
		CloseAll(user, reason);
	}

	/// <summary>Closes connections of kinds the user may no longer use.</summary>
	/// <param name="user">The online user whose permissions or restrictions changed.</param>
	internal void CloseDisallowed(User user)
	{
		if (user.EffectivePermissions(_clock.GetUtcNow()).Allows(Permissions.RoomObserve)) return;

		foreach (var connection in user.Tourneys.Where(connection => connection.IsOpen).ToList())
			Close(connection, ConnectionCloseReason.Revoked);
	}

	void IUserConnectionsPort.CloseDisallowed(User user)
	{
		CloseDisallowed(user);
	}

	ValueTask<bool> IRoomUsersPort.BlocksAsync(UserRecord blocker, UserRecord who, CancellationToken cancellationToken)
	{
		return Of(blocker).HasBlockedAsync(who, cancellationToken);
	}

	internal void Add(User user)
	{
		_online[user.Record] = user;
	}

	internal bool Remove(User user)
	{
		return _online.TryRemove(new KeyValuePair<UserRecord, User>(user.Record, user));
	}

	internal void Index(Connection connection)
	{
		_connections[connection.Token] = connection;
	}

	internal void Unindex(Connection connection)
	{
		_connections.TryRemove(new KeyValuePair<string, Connection>(connection.Token, connection));
	}

	internal Lock.Scope Enter()
	{
		return _sync.EnterScope();
	}

	private void CloseCore(Connection connection, ConnectionCloseReason reason)
	{
		if (!connection.IsOpen) return;

		connection.IsOpen = false;
		Unindex(connection);
		connection.User.Remove(connection);
		connection.User.PmChannel.ScopeTo(connection).Part();
		if (connection is BanchoConnection client)
		{
			client.ReleaseSpectators();
			client.SpectatorChannel.Close();
		}

		var wentOffline = !connection.User.Connections.Any();
		if (wentOffline)
		{
			Remove(connection.User);
			connection.User.PmChannel.Close();
		}

		_events.Publish(new UserConnectionClosed(connection, reason, wentOffline));
	}

	internal Task<(UserRecord? User, RegistrationFailure? Failure)> CreateAccountAsync(UserData data,
		Md5 passwordHash, CancellationToken cancellationToken)
	{
		return _accounts.CreateAsync(data, passwordHash, cancellationToken);
	}

	private sealed class DeferredConnectionLobbyPort(Func<IConnectionLobbyPort> resolve) : IConnectionLobbyPort
	{
		private readonly Lazy<IConnectionLobbyPort> _lobby = new(resolve);

		public Room? RoomOf(BanchoConnection player)
		{
			return _lobby.Value.RoomOf(player);
		}
	}
}
