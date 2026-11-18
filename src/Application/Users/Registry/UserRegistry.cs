using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Shared.Exceptions;
using Basil.Application.Users.Capabilities;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Ports;
using Basil.Domain.Authentication;
using Basil.Domain.Client;


using Basil.Domain.Users;

namespace Basil.Application.Users.Registry;

public sealed class UserRegistry
{
	internal static readonly TimeSpan IgnoreLogoutWithin = TimeSpan.FromSeconds(1);

	internal static readonly TimeSpan ReopenWithin = TimeSpan.FromMinutes(5);

	private static readonly OsuClient ServerClient =
		new(new OsuVersion(default, null, OsuVersionStream.Stable), default);

	private readonly IAccountRules _accounts;
	private readonly ConcurrentDictionary<long, Connection> _activity = new();
	private readonly ConnectionContext _connectionContext;
	private readonly ConcurrentDictionary<string, Connection> _connections = new(StringComparer.Ordinal);
	private readonly UserContext _context;
	private readonly ConcurrentDictionary<int, AccountGate> _gates = new();
	private readonly ConcurrentDictionary<UserRecord, User> _online = new();

	internal UserRegistry(IAccountRules accounts, UserContext context, ConnectionContext connectionContext)
	{
		_accounts = accounts;
		_context = context;
		_connectionContext = connectionContext;
	}

	public IReadOnlyDictionary<UserRecord, User> Online => _online;

	public StableConnection? OpenStableClient(UserRecord user)
	{
		return _online.GetValueOrDefault(user)?.Stable is { IsOpen: true } client ? client : null;
	}

	public IEnumerable<StableConnection> OpenStableClients()
	{
		return _online.Select(pair => OpenStableClient(pair.Key)).OfType<StableConnection>();
	}

	public Connection? Find(string token)
	{
		return _connections.GetValueOrDefault(token);
	}

	public StableConnection? FindSpectating(Connection connection)
	{
		// ponytail: scans every online osu! client; add an index if the online count grows large.
		return _online
			.Select(pair => pair.Value.Stable)
			.FirstOrDefault(host => host is not null && host.Spectators.Contains(connection));
	}

	public User Of(UserRecord record)
	{
		return _online.GetValueOrDefault(record) ?? new User(record, _context);
	}

	public ScopedUserRegistry ScopeTo(Connection actor)
	{
		return new ScopedUserRegistry(this, actor);
	}

	public ScopedUser ScopeTo(Connection actor, UserReference target)
	{
		return new ScopedUser(this, target, actor);
	}

	public async Task<LoginResult> LoginAsync(LoginAttempt attempt, SessionType type, IPAddress ip,
		OsuClient? client, int utcOffset, CancellationToken ct = default)
	{
		ThrowIfInvalidLogin(type, client);

		var (record, failure) = await _accounts.FindLoginUserAsync(attempt, ct);
		if (failure is { } loginFailure) return LoginResult.Fail(loginFailure);
		if (record is null) return LoginResult.Fail(LoginFailure.UnknownUser);

		var now = _context.Clock.GetUtcNow();
		var lifetime = (await _context.Settings.GetAsync(ct)).Sessions.ApiToken.Lifetime;
		var token = NewToken();
		var session = NewLoginSession(record, type, ip, client, now, lifetime, token);
		var connection = NewConnection(session, token, utcOffset, lifetime);

		var openFailure = Open(connection);
		if (openFailure is not null) return LoginResult.Fail(openFailure.Value);

		try
		{
			await _context.Sessions.CreateOrUpdateAsync(session);
			await RecordConnectionAsync(connection);
		}
		catch
		{
			// A login that could not be recorded does not stay open.
			Close(connection, SessionEndReason.ConnectionLost);
			throw;
		}

		return LoginResult.Success(connection);
	}

	private Task RecordConnectionAsync(Connection connection)
	{
		if (connection.IsServerOwned) return Task.CompletedTask;

		var device = connection.Session.Value.Fingerprint;
		return _context.Connections.OpenAsync(connection.Session.Id, device.Ip, device.ClientInfo,
			_context.Clock.GetUtcNow(), ReopenWithin);
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

	public async Task<LoginResult> LoginWithTokenAsync(SessionType type, string token, IPAddress ip,
		Func<UserRecord, bool>? accepts = null, CancellationToken ct = default)
	{
		if (type is not (SessionType.Api or SessionType.Irc))
			throw new ArgumentException("Only an API token or an IRC key opens a connection.", nameof(type));

		var now = _context.Clock.GetUtcNow();
		if (ReuseOpenConnection(type, token, accepts, now) is { } reused) return reused;

		var stored = await _context.Sessions.FindByTokenAsync(type, HashToken(token), now, ct);
		var record = stored is null ? null : await _context.Users.GetAsync(stored.UserId, ct);
		if (StoredTokenRefusal(stored, record, accepts) is { } refusal) return LoginResult.Fail(refusal);

		var lifetime = (await _context.Settings.GetAsync(ct)).Sessions.ApiToken.Lifetime;
		var session = ResumedSession(stored!, record!, type, ip, token);
		var connection = NewConnection(session, token, 0, lifetime);

		if (OpenUnlessRaced(token, connection) is { } settled) return settled;

		try
		{
			await RecordConnectionAsync(connection);
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
		using (Enter(connection.Record))
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

	internal static Sha256 HashToken(string token)
	{
		return new Sha256(Encoding.UTF8.GetBytes(token));
	}

	public Task<RegistrationFailure?> CheckRegistrationAsync(RegisterAttempt attempt,
		CancellationToken ct = default)
	{
		return _accounts.CheckRegistrationAsync(attempt, ct);
	}

	public async Task<(UserRecord? User, RegistrationFailure? Failure)> RegisterAsync(RegisterAttempt attempt,
		CancellationToken ct = default)
	{
		var failure = await CheckRegistrationAsync(attempt, ct);
		if (failure is not null) return (null, failure);

		return await _accounts.CreateAsync(new UserData { Name = attempt.Username }, attempt.PasswordHash,
			null);
	}

	public void Close(Connection connection, SessionEndReason reason)
	{
		if (reason is SessionEndReason.LoggedOut && connection is StableConnection &&
		    _context.Clock.GetUtcNow() - connection.Session.Value.StartedAt < IgnoreLogoutWithin)
			return;

		using var scope = Enter(connection.Record);
		CloseCore(connection, reason);
	}

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

	public int CloseIdle()
	{
		var now = _context.Clock.GetUtcNow();
		var idleConnections = _online
			.SelectMany(pair => pair.Value.Connections)
			.Where(connection => connection is { IsOpen: true, IsServerOwned: false } &&
			                     now - connection.LastActiveAt > connection.Type.IdleTimeout())
			.ToList();

		foreach (var connection in idleConnections)
			Close(connection, SessionEndReason.TimedOut);

		return idleConnections.Count;
	}

	public async Task<UserRecord> EnsureBotAsync(CancellationToken ct = default)
	{
		var stored = await _context.Users.GetAsync(BasilBot.Id, ct);
		var isBot = stored is { Value: { DeletedAt: null, Privileges: UserPrivileges.None, SilencedUntil: null } };

		if (!isBot && stored is not null) CloseAll(stored, SessionEndReason.Revoked);

		var bot = isBot
			? stored!
			: await _context.Users.ReplaceAsync(BasilBot.Id, stored is { Value.DeletedAt: null }
				? new UserData
					{ Name = stored.Value.Name, Country = stored.Value.Country, Privileges = UserPrivileges.None }
				: new UserData { Name = BasilBot.DefaultName, Privileges = UserPrivileges.None });

		await _context.Credentials.DeleteAsync(bot);
		return bot;
	}

	public async Task<OperatorConnection> OperateAsync(CancellationToken ct = default)
	{
		var bot = await _context.Users.GetAsync(BasilBot.Id, ct) ??
		          await EnsureBotAsync(ct);
		var token = NewToken();
		var login = NewServerSession(bot, SessionType.Api, token);
		return new OperatorConnection(login, token) { User = Of(bot), IsOpen = true };
	}

	public async Task<User?> ConnectBotAsync(CancellationToken ct = default)
	{
		var user = await _context.Users.GetAsync(BasilBot.Id, ct);
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

	internal LoginFailure? Open(Connection connection)
	{
		using var scope = Enter(connection.Record);

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

		if (!connection.IsServerOwned) ConnectionMetrics.Record(connection.Type, ConnectionCount.Opened);
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

		ConnectionMetrics.Record(type, ConnectionCount.Rejected);
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

	internal static string NewToken()
	{
		return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
	}

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

	public async Task FlushActivityAsync()
	{
		var sessions = new List<SessionActivity>();
		var connections = new List<ConnectionActivity>();
		foreach (var (id, _) in _activity)
			if (_activity.TryRemove(id, out var connection))
			{
				sessions.Add(new SessionActivity(id, connection.LastActiveAt, connection.ExpiresAt));
				connections.Add(new ConnectionActivity(id, connection.LastActiveAt));
			}

		await _context.Sessions.WriteActivityAsync(sessions);
		await _context.Connections.WriteActivityAsync(connections);
	}

	public async Task<int> CloseStoppedAsync()
	{
		var closed = await _context.Connections.CloseOpenAsync(SessionEndReason.ServerStopped);
		await _context.Sessions.EndOpenAsync([SessionType.Stable, SessionType.StableTourney],
			SessionEndReason.ServerStopped);
		return closed;
	}

	public async Task<int> PurgeEndedSessionsAsync()
	{
		var retention = (await _context.Settings.GetAsync()).Sessions.RetentionPeriod;
		return await _context.Sessions.DeleteEndedBeforeAsync(_context.Clock.GetUtcNow() - retention);
	}

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

	internal AccountScope Enter(UserRecord account)
	{
		return new AccountScope(_gates, account.Id);
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

		if (!connection.IsServerOwned)
		{
			ConnectionMetrics.Record(connection.Type, ConnectionCount.Closed);
			if (reason is SessionEndReason.TimedOut) ConnectionMetrics.Record(connection.Type, ConnectionCount.Timeout);
			else if (reason is SessionEndReason.ConnectionLost)
				ConnectionMetrics.Record(connection.Type, ConnectionCount.Errors);
		}

		_context.Events.Publish(new UserConnectionClosed(connection, reason, wentOffline));
	}

	private static bool OutlivesConnection(Connection connection)
	{
		return connection is { IsServerOwned: false, Type: SessionType.Api or SessionType.Irc };
	}

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
		Md5 passwordHash, UserPrivileges? privileges)
	{
		return _accounts.CreateAsync(data, passwordHash, privileges);
	}

	internal async Task<(UserRecord? User, UserOutcome Outcome)> RegisterAsync(UserData data, Md5 passwordHash,
		UserPrivileges? privileges)
	{
		var (user, failure) = await CreateAccountAsync(data, passwordHash, privileges);
		return failure switch
		{
			null => (user, UserOutcome.Ok),
			RegistrationFailure.InvalidName => (null, UserOutcome.InvalidName),
			_ => (null, UserOutcome.NameTaken)
		};
	}

	internal sealed class AccountGate
	{
		public readonly Lock Lock = new();

		public int Users;
	}

	internal readonly ref struct AccountScope : IDisposable
	{
		private readonly ConcurrentDictionary<int, AccountGate> _gates;
		private readonly int _id;
		private readonly AccountGate _gate;

		public AccountScope(ConcurrentDictionary<int, AccountGate> gates, int id)
		{
			_gates = gates;
			_id = id;
			while (true)
			{
				var gate = gates.GetOrAdd(id, _ => new AccountGate());
				// A gate whose last scope ended is marked dead (negative) before it is removed: take another one.
				if (Interlocked.Increment(ref gate.Users) < 0) continue;

				_gate = gate;
				break;
			}

			_gate.Lock.Enter();
		}

		public void Dispose()
		{
			_gate.Lock.Exit();
			if (Interlocked.Decrement(ref _gate.Users) == 0 &&
			    Interlocked.CompareExchange(ref _gate.Users, int.MinValue, 0) == 0)
				_gates.TryRemove(new KeyValuePair<int, AccountGate>(_id, _gate));
		}
	}
}