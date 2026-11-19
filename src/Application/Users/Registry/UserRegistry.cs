using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Shared.Exceptions;
using Basil.Application.Users.Capabilities;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;
using Session = Basil.Application.Users.Objects.Sessions.Session;
using SessionState = Basil.Domain.Authentication.Session;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Ports;
using Basil.Domain.Authentication;
using Basil.Domain.Game.Client;
using Basil.Domain.Users;
using Basil.Domain.Game.Users;
using Basil.Shared.Structures.Cryptography;
using Basil.Shared.Utilities;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Application.Users.Registry;

public sealed class UserRegistry
{
	internal static readonly TimeSpan IgnoreLogoutWithin = TimeSpan.FromSeconds(1);

	private static readonly OsuClient ServerClient =
		new(new OsuVersion(default, null, OsuVersionStream.Stable), default);

	private readonly IAccountRules _accounts;

	private readonly ConcurrentDictionary<long, Session> _activity = new();

	// Sessions ended here whose end the store has not confirmed yet: their tokens never resume meanwhile.
	private readonly ConcurrentDictionary<long, byte> _ended = new();

	// Accounts whose stored sessions are being revoked: those started before the revocation never resume meanwhile.
	private readonly ConcurrentDictionary<int, ImmutableList<Revocation>> _revoking = new();
	private readonly SessionContext _sessionContext;
	private readonly ConcurrentDictionary<string, Session> _tokens = new(StringComparer.Ordinal);
	private readonly UserContext _context;
	private readonly ConcurrentDictionary<int, AccountGate> _gates = new();
	private readonly ConcurrentDictionary<UserRecord, User> _online = new();

	internal UserRegistry(IAccountRules accounts, UserContext context, SessionContext sessionContext)
	{
		_accounts = accounts;
		_context = context;
		_sessionContext = sessionContext;
	}

	public IReadOnlyDictionary<UserRecord, User> Online => _online;

	public StableSession? OpenStableClient(UserRecord user)
	{
		return _online.GetValueOrDefault(user)?.Stable is { IsOpen: true } client ? client : null;
	}

	public IEnumerable<StableSession> OpenStableClients()
	{
		return _online.Select(pair => OpenStableClient(pair.Key)).OfType<StableSession>();
	}

	public Session? Find(string token)
	{
		return _tokens.GetValueOrDefault(token);
	}

	public StableSession? FindSpectating(Session connection)
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

	public ScopedUserRegistry ScopeTo(Session actor)
	{
		return new ScopedUserRegistry(this, actor);
	}

	public ScopedUser ScopeTo(Session actor, UserReference target)
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

		// After a restart a user's open API session is only in the store (the others end at startup); it is read before
		// the account gate.
		var stored = type is SessionType.Api && _online.GetValueOrDefault(record)?[type].Count is null or 0
			? await _context.Sessions.FindOpenAsync(record, type, ct)
			: null;

		var now = _context.Clock.GetUtcNow();
		var token = NewToken();
		var session = NewSession(NewLoginState(record, type, ip, client, now, token), token, utcOffset);

		var openFailure = Open(session, stored);
		if (openFailure is not null) return LoginResult.Fail(openFailure.Value);

		try
		{
			if (stored?.Ended is { } replaced)
			{
				await _context.Sessions.EndAsync(stored.Id, replaced);
				ConfirmEnded(stored.Id);
			}

			await _context.Sessions.CreateAsync(session.State);
		}
		catch
		{
			// A login that could not be recorded does not stay open.
			Close(session, SessionEndReason.ConnectionLost);
			throw;
		}

		return LoginResult.Success(session);
	}

	private static void ThrowIfInvalidLogin(SessionType type, OsuClient? client)
	{
		type.ThrowIfInvalid();
		if (type is SessionType.Stable or SessionType.StableTourney && client is null)
			throw new ArgumentNullException(nameof(client), "An osu! client login must report its client.");
	}

	private static SessionState NewLoginState(UserRecord record, SessionType type, IPAddress ip, OsuClient? client,
		DateTimeOffset now, string token)
	{
		return new SessionState
		{
			Id = NewSessionId(),
			User = record,
			Type = type,
			StartedAt = now,
			Fingerprint = new SessionFingerprint(ip, client),
			TokenHash = HashToken(token),
			LastActiveAt = now
		};
	}

	public async Task<LoginResult> ResumeAsync(string token, Func<UserRecord, bool>? accepts = null,
		CancellationToken ct = default)
	{
		const SessionType type = SessionType.Api;
		if (_tokens.GetValueOrDefault(token) is { IsOpen: true } open && open.Type == type)
			return ResumeOpen(open, accepts);

		var stored = await _context.Sessions.FindOpenByTokenAsync(type, HashToken(token), _context.Users.GetAsync, ct);
		if (stored is null || IsEnding(stored)) return LoginResult.Fail(LoginFailure.InvalidToken);

		var record = stored.User;
		if (accepts?.Invoke(record) == false) return LoginResult.Fail(LoginFailure.InvalidToken);

		var now = _context.Clock.GetUtcNow();
		var refusal = Ineligible(record, type);
		if (refusal is not null || stored.StatusAt(now) is SessionStatus.Expired)
		{
			stored.End(now, refusal is null ? SessionEndReason.TimedOut : SessionEndReason.Revoked);
			await _context.Sessions.EndAsync(stored.Id, stored.Ended!.Value);
			return LoginResult.Fail(refusal ?? LoginFailure.InvalidToken);
		}

		// Heard from now: a password login racing this resume must not judge it silent by its stored activity.
		stored.Heartbeat(now);
		var session = NewSession(stored, token, 0);
		using (Enter(record))
		{
			// Another request may have brought the same token in while this one read the database.
			if (_tokens.GetValueOrDefault(token) is { IsOpen: true } raced) return LoginResult.Success(raced);
			// The session may have been closed here after the read; its end may not have reached the store yet.
			if (IsEnding(stored)) return LoginResult.Fail(LoginFailure.InvalidToken);

			var failure = Open(session);
			if (failure is not null) return LoginResult.Fail(failure.Value);
		}

		MarkActive(session);
		return session.IsOpen ? LoginResult.Success(session) : LoginResult.Fail(LoginFailure.InvalidToken);
	}

	private LoginResult ResumeOpen(Session open, Func<UserRecord, bool>? accepts)
	{
		if (accepts?.Invoke(open.Record) == false) return LoginResult.Fail(LoginFailure.InvalidToken);

		// The online user holds the current record; the account may have changed since the session opened.
		if (Ineligible(open.User.Record, open.Type) is { } refusal)
		{
			Close(open, SessionEndReason.Revoked);
			return LoginResult.Fail(refusal);
		}

		MarkActive(open);
		return open.IsOpen ? LoginResult.Success(open) : LoginResult.Fail(LoginFailure.InvalidToken);
	}

	// The account checks a password login makes, made again for a session that comes back by its token.
	private static LoginFailure? Ineligible(UserRecord record, SessionType type)
	{
		if (record.IsBasilBot()) return LoginFailure.InvalidToken;
		if (record.Value.DeletedAt is not null) return LoginFailure.AccountDeleted;
		if (type is SessionType.StableTourney && !record.Value.Privileges.Allows(UserPrivileges.Tournament))
			return LoginFailure.NoTourneyPrivilege;
		return null;
	}

	public void ConfirmEnded(long id)
	{
		_ended.TryRemove(id, out _);
	}

	private bool IsEnding(SessionState stored)
	{
		return _ended.ContainsKey(stored.Id) ||
		       (_revoking.TryGetValue(stored.User.Id, out var pending) &&
		        pending.Any(r => stored.StartedAt <= r.At && stored.Id != r.Except));
	}

	// Called before the sessions close and their end is written, so no resume slips in between.
	internal Revocation? BeginRevoke(UserRecord user, long? only, long? except, DateTimeOffset at)
	{
		if (only is { } id)
		{
			_ended[id] = 0;
			return null;
		}

		var revocation = new Revocation(at, except);
		_revoking.AddOrUpdate(user.Id, _ => [revocation], (_, list) => list.Add(revocation));
		return revocation;
	}

	internal void EndRevoke(UserRecord user, long? only, Revocation? revocation)
	{
		if (only is { } id) ConfirmEnded(id);
		else if (revocation is not null)
		{
			while (_revoking.TryGetValue(user.Id, out var list))
			{
				var rest = list.Remove(revocation);
				if (rest.IsEmpty
					    ? _revoking.TryRemove(new KeyValuePair<int, ImmutableList<Revocation>>(user.Id, list))
					    : _revoking.TryUpdate(user.Id, rest, list)) break;
			}
		}
	}

	private Session NewSession(SessionState state, string token, int utcOffset)
	{
		return state.Type switch
		{
			SessionType.Stable => new StableSession(state, token, utcOffset, _sessionContext),
			SessionType.StableTourney => new TourneySession(state, token),
			SessionType.Irc => new IrcSession(state, token),
			_ => new ApiSession(state, token)
		};
	}

	internal static Sha256Hash HashToken(string token)
	{
		return new Sha256Hash(Encoding.UTF8.GetBytes(token));
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

	public void Close(Session connection, SessionEndReason reason)
	{
		if (reason is SessionEndReason.LoggedOut && connection is StableSession &&
		    _context.Clock.GetUtcNow() - connection.State.StartedAt < IgnoreLogoutWithin)
			return;

		using var scope = Enter(connection.Record);
		CloseCore(connection, reason);
	}

	public void MarkActive(Session session)
	{
		if (!session.IsOpen) return;

		var now = _context.Clock.GetUtcNow();
		// A session silent past its expiry is over: a late heartbeat closes it instead of reviving it.
		if (session.State.StatusAt(now) is SessionStatus.Expired)
		{
			Close(session, SessionEndReason.TimedOut);
			return;
		}

		session.State.Heartbeat(now);
		if (!session.IsServerOwned) _activity[session.State.Id] = session;
	}

	public int CloseSilent()
	{
		var now = _context.Clock.GetUtcNow();
		var expired = _online
			.SelectMany(pair => pair.Value.Connections)
			.Where(session => session is { IsOpen: true, IsServerOwned: false } &&
			                  session.State.StatusAt(now) is SessionStatus.Expired)
			.ToList();

		foreach (var session in expired)
			Close(session, SessionEndReason.TimedOut);

		return expired.Count;
	}

	public Task<int> EndSilentAsync()
	{
		return _context.Sessions.EndSilentAsync(_context.Clock.GetUtcNow());
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

	public async Task<OperatorSession> OperateAsync(CancellationToken ct = default)
	{
		var bot = await _context.Users.GetAsync(BasilBot.Id, ct) ??
		          await EnsureBotAsync(ct);
		var token = NewToken();
		var state = NewServerState(bot, SessionType.Api, token);
		return new OperatorSession(state, token) { User = Of(bot), IsOpen = true };
	}

	public async Task<User?> ConnectBotAsync(CancellationToken ct = default)
	{
		var user = await _context.Users.GetAsync(BasilBot.Id, ct);
		if (user is null) return null;

		foreach (var type in (ReadOnlySpan<SessionType>)[SessionType.Stable, SessionType.Irc])
		{
			if (_online.GetValueOrDefault(user)?[type].Any(open => open.IsOpen) is true) continue;

			var token = NewToken();
			var session = NewSession(NewServerState(user, type, token), token, 0);
			if (Open(session) is not null) return null;
		}

		return _online.GetValueOrDefault(user);
	}

	internal LoginFailure? Open(Session session, SessionState? stored = null)
	{
		using var scope = Enter(session.Record);

		// The online user holds the current record; a login may have read an older one.
		var record = _online.GetValueOrDefault(session.Record)?.Record ?? session.Record;
		if (session.Type is SessionType.StableTourney &&
		    !record.Value.Privileges.Allows(UserPrivileges.Tournament))
			return LoginFailure.NoTourneyPrivilege;

		if (TakeOver(session, stored) is { } refusal)
		{
			SessionMetrics.Record(session.Type, SessionCount.Rejected);
			SessionMetrics.Rejected.Add(1,
				new KeyValuePair<string, object?>(SessionMetrics.TypeTag, session.Type.ToString()),
				new KeyValuePair<string, object?>(SessionMetrics.ReasonTag, SessionMetrics.AlreadyOnline));
			return refusal;
		}

		// Taking over may have closed the user's last session and taken them offline.
		var user = _online.GetValueOrDefault(session.Record);
		var cameOnline = user is null;
		user ??= Of(session.Record);
		if (cameOnline) Add(user);

		session.User = user;
		user.Add(session);
		session.IsOpen = true;

		// The bot's tokens are never findable: nobody can present one, and nobody should be able to try.
		if (!session.IsServerOwned) Index(session);

		if (!session.IsServerOwned) SessionMetrics.Record(session.Type, SessionCount.Opened);
		_context.Events.Publish(new UserSessionOpened(session, cameOnline));
		return null;
	}

	// One open session per user and type: one still heard from refuses the newcomer, a silent one gives way.
	private LoginFailure? TakeOver(Session session, SessionState? stored)
	{
		var type = session.Type;
		var open = _online.GetValueOrDefault(session.Record)?[type].FirstOrDefault(other => other.IsOpen);
		if (session.IsServerOwned) return open is null ? null : LoginFailure.AlreadyOnline;
		if (!type.IsExclusive) return null;

		var now = _context.Clock.GetUtcNow();
		if (open is not null)
		{
			switch (open.State.StatusAt(now))
			{
				case SessionStatus.Active:
					return LoginFailure.AlreadyOnline;
				case SessionStatus.Replaceable:
					CloseCore(open, SessionEndReason.Replaced);
					break;
				default:
					CloseCore(open, SessionEndReason.TimedOut);
					break;
			}

			return null;
		}

		if (stored is null || stored.Equals(session.State)) return null;

		switch (stored.StatusAt(now))
		{
			case SessionStatus.Active:
				return LoginFailure.AlreadyOnline;
			case SessionStatus.Replaceable:
				stored.End(now, SessionEndReason.Replaced);
				break;
			case SessionStatus.Expired:
				stored.End(now, SessionEndReason.TimedOut);
				break;
		}

		if (stored.Ended is not null) _ended[stored.Id] = 0;
		return null;
	}

	private SessionState NewServerState(UserRecord bot, SessionType type, string token)
	{
		var now = _context.Clock.GetUtcNow();
		// A game client session must report a client, and none stands behind the bot's.
		var client = type is SessionType.Stable ? ServerClient : null;
		return new SessionState
		{
			Id = NewSessionId(),
			User = bot,
			Type = type,
			StartedAt = now,
			Fingerprint = new SessionFingerprint(IPAddress.Loopback, client),
			TokenHash = HashToken(token),
			LastActiveAt = now
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
			         .Where(connection => connection.IsOpen && connection.State.Id != except &&
			                              (only is null || connection.State.Id == only))
			         .OrderByDescending(connection => connection.Type).ToList())
			Close(connection, reason);
	}

	public async Task FlushActivityAsync()
	{
		var sessions = new List<SessionActivity>();
		foreach (var (id, _) in _activity)
			if (_activity.TryRemove(id, out var session))
				sessions.Add(new SessionActivity(id, session.Type, session.State.LastActiveAt));

		await _context.Sessions.WriteActivityAsync(sessions);
	}

	public async Task<int> CloseStoppedAsync()
	{
		// Only an API session resumes by its token; osu! and IRC clients sign in again after a restart.
		var now = _context.Clock.GetUtcNow();
		var ended = 0;
		foreach (var type in new[] { SessionType.Stable, SessionType.StableTourney, SessionType.Irc })
			ended += await _context.Sessions.EndOpenAsync(type, SessionEndReason.ServerStopped, now);
		return ended;
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

	internal void Index(Session connection)
	{
		_tokens[connection.Token] = connection;
	}

	internal void Unindex(Session connection)
	{
		_tokens.TryRemove(new KeyValuePair<string, Session>(connection.Token, connection));
	}

	internal AccountScope Enter(UserRecord account)
	{
		return new AccountScope(_gates, account.Id);
	}

	private void CloseCore(Session connection, SessionEndReason reason)
	{
		if (!connection.IsOpen) return;

		connection.State.End(_context.Clock.GetUtcNow(), reason);
		if (!connection.IsServerOwned) _ended[connection.State.Id] = 0;
		_activity.TryRemove(connection.State.Id, out _);
		connection.IsOpen = false;
		Unindex(connection);
		connection.User.Remove(connection);
		if (connection is StableSession client)
			client.ReleaseSpectators();

		var wentOffline = !connection.User.Connections.Any();
		if (wentOffline) Remove(connection.User);

		if (!connection.IsServerOwned)
		{
			SessionMetrics.Record(connection.Type, SessionCount.Closed);
			if (reason is SessionEndReason.TimedOut) SessionMetrics.Record(connection.Type, SessionCount.Timeout);
			else if (reason is SessionEndReason.ConnectionLost)
				SessionMetrics.Record(connection.Type, SessionCount.Errors);
		}

		_context.Events.Publish(new UserSessionClosed(connection, reason, wentOffline));
	}

	internal static long NewSessionId()
	{
		return RandomNumberGenerator.GetInt32(int.MaxValue) * 4194304L + RandomNumberGenerator.GetInt32(4194304) + 1;
	}

	internal Task<(UserRecord? User, RegistrationFailure? Failure)> CreateAccountAsync(UserData data,
		Md5Hash passwordHash, UserPrivileges? privileges)
	{
		return _accounts.CreateAsync(data, passwordHash, privileges);
	}

	internal async Task<(UserRecord? User, UserOutcome Outcome)> RegisterAsync(UserData data, Md5Hash passwordHash,
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

	internal sealed class Revocation(DateTimeOffset at, long? except)
	{
		internal DateTimeOffset At { get; } = at;
		internal long? Except { get; } = except;
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