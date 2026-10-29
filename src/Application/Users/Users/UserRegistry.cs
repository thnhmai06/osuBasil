using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Auth.Persistence;
using Basil.Application.Users.Capabilities;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Connections;
using Basil.Domain.Auth;
using Basil.Domain.Bancho;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Users.Users;

/// <summary>The users who are online and the connections through which they act.</summary>
public sealed class UserRegistry
{
	/// <summary>How long a connection must be idle before a new login of the same kind replaces it.</summary>
	internal static readonly TimeSpan ReplaceAfterIdle = TimeSpan.FromSeconds(10);

	/// <summary>A logout an osu! client sends this soon after login is ignored.</summary>
	internal static readonly TimeSpan IgnoreLogoutWithin = TimeSpan.FromSeconds(1);

	private readonly AccountService _accounts;
	private readonly ConcurrentDictionary<string, Connection> _connections = new(StringComparer.Ordinal);
	private readonly UserContext _context;
	private readonly ConcurrentDictionary<Guid, Session> _activity = new();
	private readonly ConcurrentDictionary<UserRecord, User> _online = new();
	private readonly ConnectionContext _connectionContext;
	private readonly Lock _sync = new(); // ponytail: one lock for all logins; per-user locks if login rate ever matters

	/// <summary>Creates the registry that owns online users and their connections.</summary>
	/// <param name="accounts">The account operations that use persistence.</param>
	/// <param name="context">The ports through which the users of this registry perform their behaviour.</param>
	/// <param name="connectionContext">The ports through which the connections of this registry perform their behaviour.</param>
	internal UserRegistry(AccountService accounts, UserContext context, ConnectionContext connectionContext)
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
	public BanchoConnection? OpenBanchoClient(UserRecord user)
	{
		return _online.GetValueOrDefault(user)?.Bancho is { IsOpen: true } client ? client : null;
	}

	/// <summary>Gets the open osu! game client of every online user.</summary>
	/// <returns>The open game clients.</returns>
	public IEnumerable<BanchoConnection> OpenBanchoClients()
	{
		return _online.Keys.Select(OpenBanchoClient).OfType<BanchoConnection>();
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
	/// <exception cref="ArgumentException">
	///     <paramref name="type" /> is BasilBot's own connection, or an IRC client, which signs in with a key through
	///     <see cref="LoginWithTokenAsync" />.
	/// </exception>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="client" /> is <see langword="null" /> for an osu! or osu!tourney login.
	/// </exception>
	/// <remarks>
	///     An osu!tourney login needs <see cref="UserPermissions.RoomObserve" /> in effect. Successful logins are
	///     recorded.
	/// </remarks>
	public async Task<LoginResult> LoginAsync(LoginAttempt attempt, ConnectionType type, IPAddress ip,
		BanchoClient? client, int utcOffset, CancellationToken cancellationToken = default)
	{
		type.ThrowIfUndefined();
		switch (type)
		{
			case ConnectionType.Bot:
				throw new ArgumentException("The bot's connection cannot be requested.", nameof(type));
			case ConnectionType.Irc:
				throw new ArgumentException("An IRC client signs in with a key, not a password.", nameof(type));
			case ConnectionType.Osu or ConnectionType.Tourney when client is null:
				throw new ArgumentNullException(nameof(client), "An osu! client login must report its client.");
		}

		var (record, failure) = await _accounts.FindLoginUserAsync(attempt, cancellationToken);
		if (failure is { } loginFailure) return LoginResult.Fail(loginFailure);
		if (record is null) return LoginResult.Fail(LoginFailure.UnknownUser);

		if (client is not null && await _context.Sessions.TakeReauthenticationAsync(record,
			    new DeviceId(client.Fingerprint.UninstallHash.HashValue, client.Fingerprint.DiskSignatureHash.HashValue),
			    cancellationToken))
			return LoginResult.Fail(LoginFailure.PasswordResetRequired);

		var now = _context.Clock.GetUtcNow();
		var lifetime = (await _context.Settings.GetAsync(cancellationToken)).Sessions.ApiToken.Lifetime;
		var token = NewToken();
		var session = new Session
		{
			Id = Guid.NewGuid(),
			User = record,
			Type = type,
			Ip = ip,
			Client = client,
			StartedAt = now,
			TokenHash = type is ConnectionType.Api ? HashToken(token) : null,
			ExpiresAt = type is ConnectionType.Api ? now + lifetime : null
		};
		var connection = NewConnection(session, token, utcOffset, lifetime);

		var openFailure = Open(connection, await ActiveRestrictionsAsync(record, cancellationToken));
		if (openFailure is not null) return LoginResult.Fail(openFailure.Value);

		_ = _context.Sessions.RecordAsync(session, CancellationToken.None);
		return LoginResult.Success(connection);
	}

	/// <summary>Opens a connection for the HTTP API token or IRC key a client presents.</summary>
	/// <param name="type">The kind of client: <see cref="ConnectionType.Api" /> or <see cref="ConnectionType.Irc" />.</param>
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
	public async Task<LoginResult> LoginWithTokenAsync(ConnectionType type, string token, IPAddress ip,
		Func<UserRecord, bool>? accepts = null, CancellationToken cancellationToken = default)
	{
		if (type is not (ConnectionType.Api or ConnectionType.Irc))
			throw new ArgumentException("Only an API token or an IRC key opens a connection.", nameof(type));

		var now = _context.Clock.GetUtcNow();
		if (_connections.GetValueOrDefault(token) is { IsOpen: true } open && open.Type == type)
		{
			if (accepts?.Invoke(open.Record) == false) return LoginResult.Fail(LoginFailure.InvalidToken);

			if (open.Session.ExpiresAt <= now)
			{
				Close(open, SessionEndReason.TimedOut);
				return LoginResult.Fail(LoginFailure.InvalidToken);
			}

			MarkActive(open);
			return LoginResult.Success(open);
		}

		var stored = await _context.Sessions.FindByTokenAsync(type, HashToken(token), now, cancellationToken);
		var record = stored is null ? null : await _context.Users.GetAsync(stored.UserId, cancellationToken);
		if (stored is null || record is null) return LoginResult.Fail(LoginFailure.InvalidToken);
		if (record.Value.DeletedAt is not null) return LoginResult.Fail(LoginFailure.AccountDeleted);
		if (accepts?.Invoke(record) == false) return LoginResult.Fail(LoginFailure.InvalidToken);

		var lifetime = (await _context.Settings.GetAsync(cancellationToken)).Sessions.ApiToken.Lifetime;
		var session = new Session
		{
			Id = stored.Id,
			User = record,
			Type = type,
			Ip = ip,
			ClientInfo = stored.ClientInfo,
			StartedAt = stored.StartedAt,
			TokenHash = HashToken(token),
			ExpiresAt = stored.ExpiresAt
		};
		var connection = NewConnection(session, token, 0, lifetime);

		var restrictions = await ActiveRestrictionsAsync(record, cancellationToken);
		using (Enter())
		{
			// Another request may have brought the same token in while this one read the database.
			if (_connections.GetValueOrDefault(token) is { IsOpen: true } raced) return LoginResult.Success(raced);

			var failure = Open(connection, restrictions);
			if (failure is not null) return LoginResult.Fail(failure.Value);
		}

		MarkActive(connection);
		return LoginResult.Success(connection);
	}

	private Connection NewConnection(Session session, string token, int utcOffset, TimeSpan lifetime)
	{
		return session.Type switch
		{
			ConnectionType.Osu => new BanchoConnection(session, token, utcOffset, _connectionContext),
			ConnectionType.Tourney => new TourneyConnection(session, token),
			ConnectionType.Irc => new IrcConnection(session, token),
			_ => new ApiConnection(session, token, lifetime)
		};
	}

	private async Task<List<UserRestriction>> ActiveRestrictionsAsync(UserRecord record,
		CancellationToken cancellationToken)
	{
		var now = _context.Clock.GetUtcNow();
		return (await _context.Restrictions.ListAsync(record, cancellationToken))
			.Where(restriction => restriction.Value.EndsAt is null || restriction.Value.EndsAt > now)
			.ToList();
	}

	/// <summary>Creates the stored form of a token or key.</summary>
	/// <param name="token">The token or key.</param>
	/// <returns>The SHA-256 hash of the token.</returns>
	internal static byte[] HashToken(string token)
	{
		return SHA256.HashData(Encoding.UTF8.GetBytes(token));
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
	///     This system operation needs no rights because the server itself calls it. An osu! client's logout within one
	///     second of login is ignored. Closing an already closed connection does nothing.
	/// </remarks>
	public void Close(Connection connection, SessionEndReason reason)
	{
		if (reason is SessionEndReason.LoggedOut && connection is BanchoConnection &&
		    _context.Clock.GetUtcNow() - connection.Session.StartedAt < IgnoreLogoutWithin)
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
			connection.LastActiveAt = now;
			if (connection is ApiConnection api) api.Session.ExtendExpiry(now, api.TokenLifetime);
			if (connection.Session.TokenHash is not null) _activity[connection.Session.Id] = connection.Session;
		}
	}

	/// <summary>Closes every connection that has sent nothing for too long.</summary>
	/// <returns>How many connections were closed.</returns>
	/// <remarks>
	///     This system operation needs no rights because the server itself calls it. The bot connection is not closed by
	///     this operation.
	/// </remarks>
	public int CloseIdle()
	{
		var now = _context.Clock.GetUtcNow();
		var idleConnections = _online.Values
			.SelectMany(user => user.Connections)
			.Where(connection => connection is { IsOpen: true, Type: not ConnectionType.Bot } &&
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
	///         An account that holds the id is taken over: it loses its permissions, its password and its restrictions,
	///         a deleted one is restored, and its connections are closed (the bot's own connection stays open when the
	///         account is the bot already). A live account keeps its name and country; a deleted one gets
	///         <see cref="BasilBot.DefaultName" />. When the id holds nothing, the bot is created.
	///     </para>
	///     <para>
	///         This system operation needs no rights because the server itself calls it at startup. Calling it again
	///         changes nothing.
	///     </para>
	/// </remarks>
	/// <exception cref="Basil.Application.Common.AlreadyExistsException">
	///     The id holds a deleted account or nothing, and another user already has <see cref="BasilBot.DefaultName" />.
	/// </exception>
	public async Task<UserRecord> EnsureBotAsync(CancellationToken cancellationToken = default)
	{
		var stored = await _context.Users.GetAsync(BasilBot.Id, cancellationToken);
		var isBot = stored is { Value: { DeletedAt: null, Permissions: UserPermissions.None } };

		if (!isBot && stored is not null) CloseAll(stored, SessionEndReason.Revoked);

		var bot = isBot
			? stored!
			: await _context.Users.ReplaceAsync(BasilBot.Id, stored is { Value.DeletedAt: null }
				? new UserData
					{ Name = stored.Value.Name, Country = stored.Value.Country, Permissions = UserPermissions.None }
				: new UserData { Name = BasilBot.DefaultName, Permissions = UserPermissions.None }, cancellationToken);

		var now = _context.Clock.GetUtcNow();
		foreach (var restriction in await _context.Restrictions.ListAsync(bot, cancellationToken))
		{
			if (restriction.Value.EndsAt <= now) continue;

			restriction.Value.EndsAt = restriction.Value.StartsAt > now ? restriction.Value.StartsAt : now;
			await _context.Restrictions.UpdateAsync(restriction, cancellationToken);
		}

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
		var login = new Session
		{
			Id = Guid.NewGuid(),
			User = bot,
			Type = ConnectionType.Bot,
			Ip = IPAddress.Loopback,
			StartedAt = _context.Clock.GetUtcNow()
		};
		return new OperatorConnection(login, NewToken()) { User = Of(bot), IsOpen = true };
	}

	/// <summary>Brings BasilBot online with a connection of its own.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The existing connection when BasilBot is already online, or <see langword="null" /> when its record is not
	///     stored or its connection could not be opened.
	/// </returns>
	/// <remarks>This system operation needs no rights because the server itself calls it at startup.</remarks>
	public async Task<BotConnection?> ConnectBotAsync(CancellationToken cancellationToken = default)
	{
		var user = await _context.Users.GetAsync(BasilBot.Id, cancellationToken);
		if (user is null) return null;

		var online = _online.GetValueOrDefault(user);
		if (online?.Bot is { IsOpen: true } connection) return connection;

		var login = new Session
		{
			Id = Guid.NewGuid(),
			User = user,
			Type = ConnectionType.Bot,
			Ip = IPAddress.Loopback,
			StartedAt = _context.Clock.GetUtcNow()
		};
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
	///     connection needs <see cref="UserPermissions.RoomObserve" /> in effect.
	/// </remarks>
	internal LoginFailure? Open(Connection connection, IReadOnlyList<UserRestriction> restrictions)
	{
		using var scope = Enter();

		// The online user holds the current record; a login may have read an older one.
		var record = _online.GetValueOrDefault(connection.Record)?.Record ?? connection.Record;
		if (connection.Type is ConnectionType.Tourney &&
		    !record.Value.Permissions.Effective(restrictions, _context.Clock.GetUtcNow())
			    .Allows(UserPermissions.RoomObserve))
			return LoginFailure.NoTourneyPermission;

		var user = _online.GetValueOrDefault(connection.Record);

		if (user is not null && !connection.Type.AllowsMany() &&
		    user[connection.Type].FirstOrDefault() is { } old)
		{
			if (_context.Clock.GetUtcNow() - old.LastActiveAt < ReplaceAfterIdle)
				return LoginFailure.AlreadyOnline;

			CloseCore(old, SessionEndReason.Replaced);
			user = _online.GetValueOrDefault(connection.Record);
		}

		var cameOnline = user is null;
		user ??= Of(connection.Record);
		if (cameOnline) Add(user);

		user.Restrictions.Replace(restrictions);
		connection.User = user;
		user.Add(connection);
		connection.IsOpen = true;

		// The bot's token is never findable: nobody can present it, and nobody should be able to try.
		if (connection.Type is not ConnectionType.Bot) Index(connection);

		_context.Events.Publish(new UserConnectionOpened(connection, cameOnline));
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
	/// <param name="except">A session whose connection stays open, or <see langword="null" /> to keep none.</param>
	/// <param name="requireReauthentication">Whether the devices of the closed osu! clients must enter their password again.</param>
	/// <param name="only">The one session to close, or <see langword="null" /> to close every session.</param>
	internal void CloseAll(UserRecord user, SessionEndReason reason, Guid? except = null,
		bool requireReauthentication = false, Guid? only = null)
	{
		var online = _online.GetValueOrDefault(user);
		if (online is null) return;

		foreach (var connection in online.Connections
			         .Where(connection => connection.IsOpen && connection.Session.Id != except &&
			                              (only is null || connection.Session.Id == only)).ToList())
		{
			if (requireReauthentication && connection.Type is ConnectionType.Osu or ConnectionType.Tourney)
				connection.Session.RequireReauthentication();

			Close(connection, reason);
		}
	}

	/// <summary>Stores the activity of the token sessions that were used since the last time.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>Run regularly instead of storing each request.</remarks>
	public async Task FlushActivityAsync(CancellationToken cancellationToken = default)
	{
		var batch = new List<SessionActivity>();
		foreach (var id in _activity.Keys)
			if (_activity.TryRemove(id, out var session))
				batch.Add(new SessionActivity(session.Id, session.LastActiveAt, session.ExpiresAt));

		await _context.Sessions.WriteActivityAsync(batch, cancellationToken);
	}

	/// <summary>Deletes the sessions that ended longer ago than the server settings keep them; the moderation log is never touched.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many sessions were deleted.</returns>
	/// <remarks>Run regularly; the server calls it on its own, so it needs no rights.</remarks>
	public async Task<int> PurgeEndedSessionsAsync(CancellationToken cancellationToken = default)
	{
		var retention = (await _context.Settings.GetAsync(cancellationToken)).Sessions.RetentionPeriod;
		return await _context.Sessions.DeleteEndedBeforeAsync(_context.Clock.GetUtcNow() - retention, cancellationToken);
	}

	/// <summary>Closes connections of kinds the user may no longer use.</summary>
	/// <param name="user">The online user whose permissions or restrictions changed.</param>
	internal void CloseDisallowed(User user)
	{
		if (user.EffectivePermissions(_context.Clock.GetUtcNow()).Allows(UserPermissions.RoomObserve)) return;

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

		// A token or key outlives its connection: only revoking it, or logging out of the API, ends the session.
		if (connection.Session.TokenHash is null || EndsToken(connection.Type, reason))
			connection.Session.End(_context.Clock.GetUtcNow(), reason);
		_ = _context.Sessions.UpdateAsync(connection.Session);
		_activity.TryRemove(connection.Session.Id, out _);
		connection.IsOpen = false;
		Unindex(connection);
		connection.User.Remove(connection);
		if (connection is BanchoConnection client)
			client.ReleaseSpectators();

		var wentOffline = !connection.User.Connections.Any();
		if (wentOffline) Remove(connection.User);

		_context.Events.Publish(new UserConnectionClosed(connection, reason, wentOffline));
	}

	private static bool EndsToken(ConnectionType type, SessionEndReason reason)
	{
		return reason is SessionEndReason.Revoked or SessionEndReason.CredentialsChanged or SessionEndReason.Deleted ||
		       (reason is SessionEndReason.LoggedOut && type is ConnectionType.Api);
	}

	internal Task<(UserRecord? User, RegistrationFailure? Failure)> CreateAccountAsync(UserData data,
		Md5 passwordHash, CancellationToken cancellationToken)
	{
		return _accounts.CreateAsync(data, passwordHash, cancellationToken);
	}
}
