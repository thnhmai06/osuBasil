using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Common;
using Basil.Application.Users.Capabilities;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Ports;
using Basil.Domain.Authentication;
using Basil.Domain.Client;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;

namespace Basil.Application.Users.Registry;

/// <summary>The users who are online and the connections through which they act.</summary>
public sealed class UserRegistry
{
	/// <summary>A logout an osu! client sends this soon after login is ignored.</summary>
	internal static readonly TimeSpan IgnoreLogoutWithin = TimeSpan.FromSeconds(1);

	/// <summary>A client that comes back from the same address this soon after its connection closed is the same connection.</summary>
	internal static readonly TimeSpan ReopenWithin = TimeSpan.FromMinutes(5);

	private static readonly OsuClient ServerClient =
		new(new OsuVersion(default, null, OsuVersionStream.Stable), default);

	private readonly IAccountRules _accounts;
	private readonly ConcurrentDictionary<long, Connection> _activity = new();
	private readonly ConnectionContext _connectionContext;
	private readonly ConcurrentDictionary<string, Connection> _connections = new(StringComparer.Ordinal);
	private readonly UserContext _context;
	private readonly ConcurrentDictionary<UserRecord, User> _online = new();
	private readonly Lock _sync = new(); // ponytail: one lock for all logins; per-user locks if login rate ever matters

	/// <summary>Creates the registry that owns online users and their connections.</summary>
	/// <param name="accounts">The account operations that use persistence.</param>
	/// <param name="context">The ports through which the users of this registry perform their behaviour.</param>
	/// <param name="connectionContext">The ports through which the connections of this registry perform their behaviour.</param>
	internal UserRegistry(IAccountRules accounts, UserContext context, ConnectionContext connectionContext)
	{
		_accounts = accounts;
		_context = context;
		_connectionContext = connectionContext;
	}

	/// <summary>Gets every online user by its account.</summary>
	public IReadOnlyDictionary<UserRecord, User> Online => _online;

	/// <summary>Gets the open osu! game client of an online user.</summary>
	/// <param name="user">The account to look up.</param>
	/// <returns>The open game client, or <see langword="null" /> when the user has none.</returns>
	public StableConnection? OpenStableClient(UserRecord user)
	{
		return _online.GetValueOrDefault(user)?.Stable is { IsOpen: true } client ? client : null;
	}

	/// <summary>Gets the open osu! game client of every online user.</summary>
	/// <returns>The open game clients.</returns>
	public IEnumerable<StableConnection> OpenStableClients()
	{
		return _online.Keys.Select(OpenStableClient).OfType<StableConnection>();
	}

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
	public StableConnection? FindSpectating(Connection connection)
	{
		// ponytail: scans every online osu! client; add an index if the online count grows large.
		return _online.Values
			.Select(user => user.Stable)
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
	/// <param name="actor">The connection acting.</param>
	/// <returns>A short-lived scope for creating accounts.</returns>
	public ScopedUserRegistry ScopeTo(Connection actor)
	{
		return new ScopedUserRegistry(this, actor);
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
	/// <exception cref="ArgumentException">
	///     <paramref name="type" /> is an IRC client, which signs in with a key through
	///     <see cref="LoginWithTokenAsync" />.
	/// </exception>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="client" /> is <see langword="null" /> for an osu! or osu!tourney login.
	/// </exception>
	/// <remarks>
	///     An osu!tourney login needs <see cref="UserPrivileges.Tournament" /> in effect. Successful logins are
	///     recorded.
	/// </remarks>
	public async Task<LoginResult> LoginAsync(LoginAttempt attempt, SessionType type, IPAddress ip,
		OsuClient? client, int utcOffset, CancellationToken cancellationToken = default)
	{
		ThrowIfInvalidLogin(type, client);

		var (record, failure) = await _accounts.FindLoginUserAsync(attempt, cancellationToken);
		if (failure is { } loginFailure) return LoginResult.Fail(loginFailure);
		if (record is null) return LoginResult.Fail(LoginFailure.UnknownUser);

		var now = _context.Clock.GetUtcNow();
		var lifetime = (await _context.Settings.GetAsync(cancellationToken)).Sessions.ApiToken.Lifetime;
		var token = NewToken();
		var session = NewLoginSession(record, type, ip, client, now, lifetime, token);
		var connection = NewConnection(session, token, utcOffset, lifetime);

		var openFailure = Open(connection);
		if (openFailure is not null) return LoginResult.Fail(openFailure.Value);

		try
		{
			await _context.Sessions.CreateOrUpdateAsync(session, cancellationToken);
			await RecordConnectionAsync(connection, cancellationToken);
		}
		catch
		{
			// A login that could not be recorded does not stay open.
			Close(connection, SessionEndReason.ConnectionLost);
			throw;
		}

		return LoginResult.Success(connection);
	}

	private Task RecordConnectionAsync(Connection connection, CancellationToken cancellationToken)
	{
		if (connection.IsServerOwned) return Task.CompletedTask;

		var device = connection.Session.Value.Fingerprint;
		return _context.Connections.OpenAsync(connection.Session.Id, device.Ip, device.ClientInfo,
			_context.Clock.GetUtcNow(), ReopenWithin, cancellationToken);
	}

	private static void ThrowIfInvalidLogin(SessionType type, OsuClient? client)
	{
		type.ThrowIfUndefined();
		switch (type)
		{
			case SessionType.Irc:
				throw new ArgumentException("An IRC client signs in with a key, not a password.", nameof(type));
			case SessionType.Stable or SessionType.StableTourney when client is null:
				throw new ArgumentNullException(nameof(client), "An osu! client login must report its client.");
		}
	}

	private static SessionRecord NewLoginSession(UserRecord record, SessionType type, IPAddress ip, OsuClient? client,
		DateTimeOffset now, TimeSpan lifetime, string token)
	{
		// Only an API token expires; an osu! session lasts as long as its connection.
		DateTimeOffset? expiresAt = type is SessionType.Api ? now + lifetime : null;
		return new SessionRecord
		{
			Id = NewSessionId(),
			Value = new SessionData(record, type, now, new SessionFingerprint(ip, client), HashToken(token), expiresAt,
				now)
		};
	}

	/// <summary>Opens a connection for the HTTP API token or IRC key a client presents.</summary>
	/// <param name="type">The kind of client: <see cref="SessionType.Api" /> or <see cref="SessionType.Irc" />.</param>
	/// <param name="token">The token or key the client presented.</param>
	/// <param name="ip">The IP address the client came from.</param>
	/// <param name="accepts">
	///     Decides, before anything is opened, whether the key's owner is the account the client claims to be;
	///     <see langword="null" /> accepts every owner.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The connection the token opens, or why none could be opened.</returns>
	/// <remarks>
	///     A token that is not held by an open connection is verified against the stored sessions; only a valid one
	///     is brought into memory. A token whose session has ended or expired opens nothing, and so does a token whose
	///     owner <paramref name="accepts" /> refuses (<see cref="LoginFailure.InvalidToken" />). Using an API token
	///     renews its expiry.
	/// </remarks>
	/// <exception cref="ArgumentException"><paramref name="type" /> is not Api or Irc.</exception>
	public async Task<LoginResult> LoginWithTokenAsync(SessionType type, string token, IPAddress ip,
		Func<UserRecord, bool>? accepts = null, CancellationToken cancellationToken = default)
	{
		if (type is not (SessionType.Api or SessionType.Irc))
			throw new ArgumentException("Only an API token or an IRC key opens a connection.", nameof(type));

		var now = _context.Clock.GetUtcNow();
		if (ReuseOpenConnection(type, token, accepts, now) is { } reused) return reused;

		var stored = await _context.Sessions.FindByTokenAsync(type, HashToken(token), now, cancellationToken);
		var record = stored is null ? null : await _context.Users.GetAsync(stored.UserId, cancellationToken);
		if (StoredTokenRefusal(stored, record, accepts) is { } refusal) return LoginResult.Fail(refusal);

		var lifetime = (await _context.Settings.GetAsync(cancellationToken)).Sessions.ApiToken.Lifetime;
		var session = ResumedSession(stored!, record!, type, ip, token);
		var connection = NewConnection(session, token, 0, lifetime);

		if (OpenUnlessRaced(token, connection) is { } settled) return settled;

		try
		{
			await RecordConnectionAsync(connection, cancellationToken);
		}
		catch
		{
			// A connection that could not be recorded does not stay open.
			Close(connection, SessionEndReason.ConnectionLost);
			throw;
		}

		MarkActive(connection);
		return LoginResult.Success(connection);
	}

	private LoginResult? ReuseOpenConnection(SessionType type, string token, Func<UserRecord, bool>? accepts,
		DateTimeOffset now)
	{
		if (_connections.GetValueOrDefault(token) is not { IsOpen: true } open || open.Type != type) return null;

		if (accepts?.Invoke(open.Record) == false) return LoginResult.Fail(LoginFailure.InvalidToken);

		if (open.ExpiresAt <= now)
		{
			Close(open, SessionEndReason.TimedOut);
			return LoginResult.Fail(LoginFailure.InvalidToken);
		}

		MarkActive(open);
		return LoginResult.Success(open);
	}

	private static LoginFailure? StoredTokenRefusal(StoredSession? stored, UserRecord? record,
		Func<UserRecord, bool>? accepts)
	{
		if (stored is null || record is null) return LoginFailure.InvalidToken;
		if (record.Value.DeletedAt is not null) return LoginFailure.AccountDeleted;
		if (accepts?.Invoke(record) == false) return LoginFailure.InvalidToken;
		return null;
	}

	private static SessionRecord ResumedSession(StoredSession stored, UserRecord record, SessionType type, IPAddress ip,
		string token)
	{
		return new SessionRecord
		{
			Id = stored.Id,
			Value = new SessionData(record, type, stored.StartedAt,
				new SessionFingerprint(ip, ClientInfo: stored.ClientInfo), HashToken(token), stored.ExpiresAt,
				stored.StartedAt)
		};
	}

	private LoginResult? OpenUnlessRaced(string token, Connection connection)
	{
		using (Enter())
		{
			// Another request may have brought the same token in while this one read the database.
			if (_connections.GetValueOrDefault(token) is { IsOpen: true } raced) return LoginResult.Success(raced);

			var failure = Open(connection);
			if (failure is not null) return LoginResult.Fail(failure.Value);
		}

		return null;
	}

	private Connection NewConnection(SessionRecord session, string token, int utcOffset, TimeSpan lifetime)
	{
		return session.Value.Type switch
		{
			SessionType.Stable => new StableConnection(session, token, utcOffset, _connectionContext),
			SessionType.StableTourney => new TourneyConnection(session, token),
			SessionType.Irc => new IrcConnection(session, token),
			_ => new ApiConnection(session, token, lifetime)
		};
	}

	/// <summary>Creates the stored form of a token or key.</summary>
	/// <param name="token">The token or key.</param>
	/// <returns>The SHA-256 hash of the token.</returns>
	internal static Sha256 HashToken(string token)
	{
		return new Sha256(Encoding.UTF8.GetBytes(token));
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
			null, cancellationToken);
	}

	/// <summary>Closes a connection, taking its user offline if it was their last one.</summary>
	/// <param name="connection">The connection to close.</param>
	/// <param name="reason">Why the connection is closed.</param>
	/// <remarks>
	///     This system operation needs no rights because the server itself calls it. An osu! client's logout within one
	///     second of login is ignored. Closing an already closed connection does nothing.
	/// </remarks>
	public void Close(Connection connection, SessionEndReason reason)
	{
		if (reason is SessionEndReason.LoggedOut && connection is StableConnection &&
		    _context.Clock.GetUtcNow() - connection.Session.Value.StartedAt < IgnoreLogoutWithin)
			return;

		using var scope = Enter();
		CloseCore(connection, reason);
	}

	/// <summary>Records that a client just sent something.</summary>
	/// <param name="connection">The connection that sent a message.</param>
	/// <remarks>This system operation needs no rights because the server itself calls it.</remarks>
	public void MarkActive(Connection connection)
	{
		if (connection.IsOpen)
		{
			var now = _context.Clock.GetUtcNow();
			connection.MarkActive(now);
			if (connection is ApiConnection api) api.ExtendExpiry(now, api.TokenLifetime);
			if (!connection.IsServerOwned) _activity[connection.Session.Id] = connection;
		}
	}

	/// <summary>Closes every connection that has sent nothing for too long.</summary>
	/// <returns>How many connections were closed.</returns>
	/// <remarks>
	///     This system operation needs no rights because the server itself calls it. BasilBot's connections are not closed
	///     by this operation.
	/// </remarks>
	public int CloseIdle()
	{
		var now = _context.Clock.GetUtcNow();
		var idleConnections = _online.Values
			.SelectMany(user => user.Connections)
			.Where(connection => connection is { IsOpen: true, IsServerOwned: false } &&
			                     now - connection.LastActiveAt > connection.Type.IdleTimeout())
			.ToList();

		foreach (var connection in idleConnections)
			Close(connection, SessionEndReason.TimedOut);

		return idleConnections.Count;
	}

	/// <summary>Makes the account with BasilBot's fixed id the bot, whatever that id holds.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The bot's account.</returns>
	/// <remarks>
	///     <para>
	///         An account that holds the id is taken over: it loses its privileges, its password and its silence,
	///         a deleted one is restored, and its connections are closed (the bot's own connections stay open when the
	///         account is the bot already). A live account keeps its name and country; a deleted one gets
	///         <see cref="BasilBot.DefaultName" />. When the id holds nothing, the bot is created.
	///     </para>
	///     <para>
	///         This system operation needs no rights because the server itself calls it at startup. Calling it again
	///         changes nothing.
	///     </para>
	/// </remarks>
	/// <exception cref="AlreadyExistsException">
	///     The id holds a deleted account or nothing, and another user already has <see cref="BasilBot.DefaultName" />.
	/// </exception>
	public async Task<UserRecord> EnsureBotAsync(CancellationToken cancellationToken = default)
	{
		var stored = await _context.Users.GetAsync(BasilBot.Id, cancellationToken);
		var isBot = stored is { Value: { DeletedAt: null, Privileges: UserPrivileges.None, SilencedUntil: null } };

		if (!isBot && stored is not null) CloseAll(stored, SessionEndReason.Revoked);

		var bot = isBot
			? stored!
			: await _context.Users.ReplaceAsync(BasilBot.Id, stored is { Value.DeletedAt: null }
				? new UserData
					{ Name = stored.Value.Name, Country = stored.Value.Country, Privileges = UserPrivileges.None }
				: new UserData { Name = BasilBot.DefaultName, Privileges = UserPrivileges.None }, cancellationToken);

		await _context.Credentials.DeleteAsync(bot, cancellationToken);
		return bot;
	}

	/// <summary>Opens the operator's connection, which acts as BasilBot and skips the rights checks.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A connection for the console; it is not registered, so it never appears online.</returns>
	/// <remarks>
	///     Only the server's own console calls this. BasilBot's account is created first when it does not exist yet, as the
	///     server does at startup.
	/// </remarks>
	public async Task<OperatorConnection> OperateAsync(CancellationToken cancellationToken = default)
	{
		var bot = await _context.Users.GetAsync(BasilBot.Id, cancellationToken) ??
		          await EnsureBotAsync(cancellationToken);
		var token = NewToken();
		var login = NewServerSession(bot, SessionType.Api, token);
		return new OperatorConnection(login, token) { User = Of(bot), IsOpen = true };
	}

	/// <summary>Brings BasilBot online with one osu! game client connection and one IRC connection of its own.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The online bot, or <see langword="null" /> when its record is not stored or a connection could not be opened.
	/// </returns>
	/// <remarks>
	///     This system operation needs no rights because the server itself calls it at startup. Only a connection that is
	///     missing is opened, the game client one first, so calling it again changes nothing. No transport sits behind
	///     either connection.
	/// </remarks>
	public async Task<User?> ConnectBotAsync(CancellationToken cancellationToken = default)
	{
		var user = await _context.Users.GetAsync(BasilBot.Id, cancellationToken);
		if (user is null) return null;

		foreach (var type in (ReadOnlySpan<SessionType>)[SessionType.Stable, SessionType.Irc])
		{
			if (_online.GetValueOrDefault(user)?[type].Any(open => open.IsOpen) is true) continue;

			var token = NewToken();
			var login = NewServerSession(user, type, token);
			Connection connection = type is SessionType.Stable
				? new StableConnection(login, token, 0, _connectionContext)
				: new IrcConnection(login, token);
			if (Open(connection) is not null) return null;
		}

		return _online.GetValueOrDefault(user);
	}

	/// <summary>Opens an authenticated connection, bringing its user online if this is their first.</summary>
	/// <param name="connection">The new connection.</param>
	/// <returns><see langword="null" /> on success; otherwise, why the connection was refused.</returns>
	/// <remarks>
	///     A user holds at most the number of open connections of a kind that the server settings allow. At the limit,
	///     the user's connections of that kind that sent nothing for too long are closed first; if the user is still at
	///     the limit, an HTTP API connection replaces the one that was active longest ago and any other kind is refused
	///     with <see cref="LoginFailure.TooManySessions" />. BasilBot's connections are limited to one of each kind
	///     whatever the settings say. An osu!tourney connection needs <see cref="UserPrivileges.Tournament" /> in
	///     effect.
	/// </remarks>
	internal LoginFailure? Open(Connection connection)
	{
		using var scope = Enter();

		// The online user holds the current record; a login may have read an older one.
		var record = _online.GetValueOrDefault(connection.Record)?.Record ?? connection.Record;
		if (connection.Type is SessionType.StableTourney &&
		    !record.Value.Privileges.Allows(UserPrivileges.Tournament))
			return LoginFailure.NoTourneyPrivilege;

		if (_online.GetValueOrDefault(connection.Record) is { } holder &&
		    MakeRoomFor(holder, connection) is { } refusal)
			return refusal;

		// Making room may have closed the user's last connection and taken them offline.
		var user = _online.GetValueOrDefault(connection.Record);
		var cameOnline = user is null;
		user ??= Of(connection.Record);
		if (cameOnline) Add(user);

		connection.User = user;
		user.Add(connection);
		connection.IsOpen = true;

		// The bot's tokens are never findable: nobody can present one, and nobody should be able to try.
		if (!connection.IsServerOwned) Index(connection);

		_context.Events.Publish(new UserConnectionOpened(connection, cameOnline));
		return null;
	}

	private LoginFailure? MakeRoomFor(User user, Connection connection)
	{
		var type = connection.Type;
		var isBot = connection.IsServerOwned;
		var max = isBot ? 1 : _context.Settings.Current.Sessions.MaxPerUser.For(type);
		if (user[type].Count < max) return null;

		var now = _context.Clock.GetUtcNow();
		if (!isBot)
			foreach (var idle in user[type].Where(open => now - open.LastActiveAt > type.IdleTimeout()).ToList())
				CloseCore(idle, SessionEndReason.TimedOut);

		var current = _online.GetValueOrDefault(connection.Record);
		if (current is null || current[type].Count < max) return null;

		if (type is SessionType.Api)
		{
			CloseCore(current[type].MinBy(open => open.LastActiveAt)!, SessionEndReason.Replaced);
			return null;
		}

		ConnectionMetrics.Rejected.Add(1, new KeyValuePair<string, object?>(ConnectionMetrics.TypeTag, type.ToString()),
			new KeyValuePair<string, object?>(ConnectionMetrics.ReasonTag, ConnectionMetrics.TooManySessions));
		return LoginFailure.TooManySessions;
	}

	private SessionRecord NewServerSession(UserRecord bot, SessionType type, string token)
	{
		var now = _context.Clock.GetUtcNow();
		// A game client session must report a client, and none stands behind the bot's.
		var client = type is SessionType.Stable ? ServerClient : null;
		return new SessionRecord
		{
			Id = NewSessionId(),
			Value = new SessionData(bot, type, now, new SessionFingerprint(IPAddress.Loopback, client),
				HashToken(token), null, now)
		};
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
	/// <param name="except">A session whose connection stays open, or <see langword="null" /> to keep none.</param>
	/// <param name="only">The one session to close, or <see langword="null" /> to close every session.</param>
	internal void CloseAll(UserRecord user, SessionEndReason reason, long? except = null, long? only = null)
	{
		var online = _online.GetValueOrDefault(user);
		if (online is null) return;

		// The osu! game client goes last: the user's logout reaches the other clients only when it closes last.
		foreach (var connection in online.Connections
			         .Where(connection => connection.IsOpen && connection.Session.Id != except &&
			                              (only is null || connection.Session.Id == only))
			         .OrderByDescending(connection => connection.Type).ToList())
			Close(connection, reason);
	}

	/// <summary>Stores the activity of the sessions and connections that were used since the last time.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>Run regularly instead of storing each request.</remarks>
	public async Task FlushActivityAsync(CancellationToken cancellationToken = default)
	{
		var sessions = new List<SessionActivity>();
		var connections = new List<ConnectionActivity>();
		foreach (var id in _activity.Keys)
			if (_activity.TryRemove(id, out var connection))
			{
				sessions.Add(new SessionActivity(id, connection.LastActiveAt, connection.ExpiresAt));
				connections.Add(new ConnectionActivity(id, connection.LastActiveAt));
			}

		await _context.Sessions.WriteActivityAsync(sessions, cancellationToken);
		await _context.Connections.WriteActivityAsync(connections, cancellationToken);
	}

	/// <summary>
	///     Ends what a stop of the server left open: every connection still open is closed at the moment it was last
	///     active, and so is the session of every osu! client, which lives only as long as its connection.
	/// </summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many connections were closed.</returns>
	/// <remarks>
	///     This system operation needs no rights because the server itself calls it at startup. API tokens and IRC keys
	///     stay valid.
	/// </remarks>
	public async Task<int> CloseStoppedAsync(CancellationToken cancellationToken = default)
	{
		var closed = await _context.Connections.CloseOpenAsync(SessionEndReason.ServerStopped, cancellationToken);
		await _context.Sessions.EndOpenAsync([SessionType.Stable, SessionType.StableTourney],
			SessionEndReason.ServerStopped, cancellationToken);
		return closed;
	}

	/// <summary>
	///     Deletes the sessions that ended longer ago than the server settings keep them; the moderation log is never
	///     touched.
	/// </summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many sessions were deleted.</returns>
	/// <remarks>Run regularly; the server calls it on its own, so it needs no rights.</remarks>
	public async Task<int> PurgeEndedSessionsAsync(CancellationToken cancellationToken = default)
	{
		var retention = (await _context.Settings.GetAsync(cancellationToken)).Sessions.RetentionPeriod;
		return await _context.Sessions.DeleteEndedBeforeAsync(_context.Clock.GetUtcNow() - retention,
			cancellationToken);
	}

	/// <summary>Closes connections of kinds the user may no longer use.</summary>
	/// <param name="user">The online user whose privileges or silence changed.</param>
	internal void CloseDisallowed(User user)
	{
		if (user.EffectivePrivileges(_context.Clock.GetUtcNow()).Allows(UserPrivileges.Tournament)) return;

		foreach (var connection in user.Tourneys.Where(connection => connection.IsOpen).ToList())
			Close(connection, SessionEndReason.Revoked);
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

	private void CloseCore(Connection connection, SessionEndReason reason)
	{
		if (!connection.IsOpen) return;

		// An API token or IRC key outlives its connection: only revoking it, or logging out of the API, ends the
		// session. An osu! session lives as long as its connection.
		if (!OutlivesConnection(connection) || EndsToken(connection.Type, reason))
			connection.EndSession(_context.Clock.GetUtcNow(), reason);
		_activity.TryRemove(connection.Session.Id, out _);
		connection.IsOpen = false;
		Unindex(connection);
		connection.User.Remove(connection);
		if (connection is StableConnection client)
			client.ReleaseSpectators();

		var wentOffline = !connection.User.Connections.Any();
		if (wentOffline) Remove(connection.User);

		_context.Events.Publish(new UserConnectionClosed(connection, reason, wentOffline));
	}

	private static bool OutlivesConnection(Connection connection)
	{
		return connection is { IsServerOwned: false, Type: SessionType.Api or SessionType.Irc };
	}

	/// <summary>Creates the identifier of a new session.</summary>
	/// <returns>
	///     A positive number a browser can hold without losing a digit; it marks the session on the API so that the hash
	///     of its token is never shown.
	/// </returns>
	internal static long NewSessionId()
	{
		return RandomNumberGenerator.GetInt32(int.MaxValue) * 4194304L + RandomNumberGenerator.GetInt32(4194304) + 1;
	}

	private static bool EndsToken(SessionType type, SessionEndReason reason)
	{
		return reason is SessionEndReason.Revoked or SessionEndReason.CredentialsChanged or SessionEndReason.Deleted ||
		       (reason is SessionEndReason.LoggedOut or SessionEndReason.Replaced && type is SessionType.Api);
	}

	internal Task<(UserRecord? User, RegistrationFailure? Failure)> CreateAccountAsync(UserData data,
		Md5 passwordHash, UserPrivileges? privileges, CancellationToken cancellationToken)
	{
		return _accounts.CreateAsync(data, passwordHash, privileges, cancellationToken);
	}

	/// <summary>Creates an account and reports the outcome as a user outcome.</summary>
	/// <param name="data">The account data.</param>
	/// <param name="passwordHash">The MD5 digest of the password.</param>
	/// <param name="privileges">
	///     The privileges of the account, or <see langword="null" /> for the default privileges of new accounts.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The new account when the result is ok; otherwise why none was created.</returns>
	internal async Task<(UserRecord? User, UserOutcome Outcome)> RegisterAsync(UserData data, Md5 passwordHash,
		UserPrivileges? privileges, CancellationToken cancellationToken)
	{
		var (user, failure) = await CreateAccountAsync(data, passwordHash, privileges, cancellationToken);
		return failure switch
		{
			null => (user, UserOutcome.Ok),
			RegistrationFailure.InvalidName => (null, UserOutcome.InvalidName),
			_ => (null, UserOutcome.NameTaken)
		};
	}
}