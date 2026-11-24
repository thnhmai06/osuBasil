using Basil.Application.Sessions.Capabilities;
using Basil.Application.Sessions.Objects.Events;
using Basil.Application.Sessions.Ports;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Sessions.Persistence;
using Basil.Application.Users.Registry;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Sessions;
using Basil.Domain.Client;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;
using Basil.Shared.Utilities;
using Session = Basil.Application.Sessions.Objects.Session;
using SessionState = Basil.Domain.Sessions.Session;

namespace Basil.Application.Sessions.Registry;

/// <summary>Owns the live sessions: who is online, their tokens, sign-in and resume, take-over, revocation and registration.</summary>
public sealed class SessionRegistry
{
	internal static readonly TimeSpan IgnoreLogoutWithin = TimeSpan.FromSeconds(1);

	private readonly IAccountRules _accounts;

	private readonly ActivityLog _activity;
	private readonly AccountRegistration _registration;
	private readonly SessionContext _context;

	// Sessions ended here whose end the store has not confirmed yet: their tokens never resume meanwhile.
	private readonly ConcurrentDictionary<long, byte> _ended = new();
	private readonly ConcurrentDictionary<int, AccountGate> _gates = new();
	private readonly ConcurrentDictionary<UserRecord, UserSessions> _online = new();

	// Accounts whose stored sessions are being revoked: those started before the revocation never resume meanwhile.
	private readonly ConcurrentDictionary<int, ImmutableList<Revocation>> _revoking = new();
	private readonly ConcurrentDictionary<string, Session> _tokens = new(StringComparer.Ordinal);
	private readonly UserRegistry _users;

	internal SessionRegistry(IAccountRules accounts, AccountRegistration registration, ActivityLog activity,
		SessionContext context, UserRegistry users)
	{
		_accounts = accounts;
		_registration = registration;
		_activity = activity;
		_context = context;
		_users = users;
	}

	/// <summary>The users who are online; a session silent past its expiry is closed on the way and does not count.</summary>
	public IReadOnlyDictionary<UserRecord, UserSessions> Online
	{
		get
		{
			CloseExpired(_online.Values.SelectMany(user => user.Connections));
			return _online;
		}
	}

	internal IReadOnlyDictionary<UserRecord, UserSessions> OnlineNow => _online;

	public StableSession? OpenStableClient(UserRecord user)
	{
		return Live(user)?.Stable is { IsOpen: true } client ? client : null;
	}

	public IEnumerable<StableSession> OpenStableClients()
	{
		return Online.Select(pair => OpenStableClient(pair.Key)).OfType<StableSession>();
	}

	public Session? Find(string token)
	{
		var session = _tokens.GetValueOrDefault(token);
		return session is not null && CloseExpired([session]) == 0 ? session : null;
	}

	public StableSession? FindSpectating(Session connection)
	{
		// ponytail: scans every online osu! client; add an index if the online count grows large.
		return _online
			.Select(pair => pair.Value.Stable)
			.FirstOrDefault(host => host is not null && host.Spectators.Contains(connection));
	}

	/// <summary>The sessions of an account: its online ones, or an empty set when it is offline.</summary>
	public UserSessions Of(UserRecord record)
	{
		return Live(record) ?? new UserSessions(_users.Of(record), _context);
	}

	private UserSessions? Live(UserRecord record)
	{
		if (_online.GetValueOrDefault(record) is { } online) CloseExpired(online.Connections);
		return _online.GetValueOrDefault(record);
	}

	public ScopedSessionRegistry ScopeTo(Session actor)
	{
		return new ScopedSessionRegistry(_registration, _context, actor);
	}

	public ScopedUserSessions ScopeSessions(Session actor, int targetId)
	{
		return new ScopedUserSessions(this, _users.ScopeTo(actor, targetId), actor);
	}

	public ScopedUserCredentials ScopeCredentials(Session actor, int targetId)
	{
		return new ScopedUserCredentials(this, _users.ScopeTo(actor, targetId), actor);
	}

	internal async Task<LoginResult> LoginAsync(LoginAttempt attempt, SessionType type, IPAddress ip,
		ClientBuild? client, int utcOffset, CancellationToken ct = default)
	{
		ThrowIfInvalidLogin(type, client);

		var (record, failure) = await _accounts.FindLoginUserAsync(attempt, ct);
		if (failure is { } loginFailure) return LoginResult.Fail(loginFailure);
		if (record is null) return LoginResult.Fail(LoginFailure.UnknownUser);

		// After a restart a user's open API session is only in the store (the others end at startup); it is read before
		// the account gate.
		var stored = type is SessionType.Api && _online.GetValueOrDefault(record)?[type].Count is null or 0
			? Wrap(await _context.Sessions.FindOpenAsync(record, type, ct))
			: null;

		// The client asks for its friends at sign-in; they are answered from memory.
		await _context.Relationships.EnsureLoadedAsync(record, ct);

		var now = _context.Clock.GetUtcNow();
		var token = NewToken();
		var session = NewSession(NewLoginState(record, type, ip, client, now, token), token, utcOffset);

		var openFailure = Open(session, stored);
		if (openFailure is not null) return LoginResult.Fail(openFailure.Value);

		try
		{
			if (stored?.Record.Value.Ended is { } replaced)
			{
				await _context.Sessions.EndAsync(stored.Record.Id, replaced);
				ConfirmEnded(stored.Record.Id);
			}

			await _context.Sessions.CreateAsync(session.State.Record);
		}
		catch
		{
			// A login that could not be recorded does not stay open.
			Close(session, SessionEndReason.ConnectionLost);
			throw;
		}

		return LoginResult.Success(session);
	}

	private static void ThrowIfInvalidLogin(SessionType type, ClientBuild? client)
	{
		type.ThrowIfInvalid();
		if (type is SessionType.Stable or SessionType.StableTourney && client is null)
			throw new ArgumentNullException(nameof(client), "An osu! client login must report its client.");
	}

	private static SessionState? Wrap(SessionRecord? record)
	{
		return record is null ? null : new SessionState(record);
	}

	private static SessionState NewLoginState(UserRecord record, SessionType type, IPAddress ip, ClientBuild? client,
		DateTimeOffset now, string token)
	{
		return SessionState.Start(NewSessionId(), record, type, new SessionFingerprint(ip, client), HashToken(token),
			now);
	}

	internal async Task<LoginResult> ResumeAsync(string token, Func<UserRecord, bool>? accepts = null,
		CancellationToken ct = default)
	{
		const SessionType type = SessionType.Api;
		if (_tokens.GetValueOrDefault(token) is { IsOpen: true } open && open.Type == type)
			return ResumeOpen(open, accepts);

		var stored =
			Wrap(await _context.Sessions.FindOpenByTokenAsync(type, HashToken(token), _context.Users.GetAsync, ct));
		if (stored is null || IsEnding(stored)) return LoginResult.Fail(LoginFailure.InvalidToken);

		var record = stored.Record.Value.User;
		if (accepts?.Invoke(record) == false) return LoginResult.Fail(LoginFailure.InvalidToken);

		var now = _context.Clock.GetUtcNow();
		var refusal = Ineligible(record, type);
		if (refusal is not null) stored.End(now, SessionEndReason.Revoked);
		// Heard from now: a password login racing this resume must not judge it silent by its stored activity.
		else if (stored.Heartbeat(now) is SessionActionResult.Expired) refusal = LoginFailure.InvalidToken;

		if (refusal is not null)
		{
			await _context.Sessions.EndAsync(stored.Record.Id, stored.Record.Value.Ended!.Value);
			return LoginResult.Fail(refusal.Value);
		}

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
		if (record.Id == BasilBot.Id) return LoginFailure.InvalidToken;
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
		return _ended.ContainsKey(stored.Record.Id) ||
		       (_revoking.TryGetValue(stored.Record.Value.User.Id, out var pending) &&
		        pending.Any(r => stored.Record.Value.StartedAt <= r.At && stored.Record.Id != r.Except));
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
			while (_revoking.TryGetValue(user.Id, out var list))
			{
				var rest = list.Remove(revocation);
				if (rest.IsEmpty
					    ? _revoking.TryRemove(new KeyValuePair<int, ImmutableList<Revocation>>(user.Id, list))
					    : _revoking.TryUpdate(user.Id, rest, list)) break;
			}
	}

	internal Session NewSession(SessionState state, string token, int utcOffset)
	{
		return state.Record.Value.Type switch
		{
			SessionType.Stable => new StableSession(state, token, utcOffset, _context),
			SessionType.StableTourney => new TourneySession(state, token),
			SessionType.Irc => new IrcSession(state, token),
			_ => new ApiSession(state, token)
		};
	}

	internal static Sha256Hash HashToken(string token)
	{
		return new Sha256Hash(Encoding.UTF8.GetBytes(token));
	}

	internal void Close(Session connection, SessionEndReason reason)
	{
		if (reason is SessionEndReason.LoggedOut && connection is StableSession &&
		    _context.Clock.GetUtcNow() - connection.State.Record.Value.StartedAt < IgnoreLogoutWithin)
			return;

		using var scope = Enter(connection.Record);
		CloseCore(connection, reason);
	}

	internal void MarkActive(Session session)
	{
		if (!session.IsOpen) return;

		// A session silent past its expiry is over: a late heartbeat closes it instead of reviving it.
		if (session.Heartbeat(_context.Clock.GetUtcNow()) is SessionActionResult.Expired)
		{
			Close(session, SessionEndReason.TimedOut);
			return;
		}

		if (!session.IsServerOwned) _activity.Record(session);
	}

	// Expiry is decided when a session is looked at, not by a timer: a silent one is closed here and counts as offline.
	private int CloseExpired(IEnumerable<Session> sessions)
	{
		var now = _context.Clock.GetUtcNow();
		var expired = sessions
			.Where(session => session is { IsOpen: true, IsServerOwned: false } &&
			                  session.StatusAt(now) is SessionStatus.Expired)
			.ToList();

		foreach (var session in expired)
			Close(session, SessionEndReason.TimedOut);

		return expired.Count;
	}

	internal LoginFailure? Open(Session session, SessionState? stored = null)
	{
		using var scope = Enter(session.Record);

		// The online user holds the current record; a login may have read an older one.
		var record = _online.GetValueOrDefault(session.Record)?.User.Record ?? session.Record;
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
		user ??= new UserSessions(_users.Of(session.Record), _context);
		if (cameOnline) Add(user);

		session.User = user.User;
		session.Siblings = user;
		user.Add(session);
		session.IsOpen = true;

		// The bot's tokens are never findable: nobody can present one, and nobody should be able to try.
		if (!session.IsServerOwned) Index(session);

		if (!session.IsServerOwned) SessionMetrics.Record(session.Type, SessionCount.Opened);
		_context.Events.Publish(new SessionOpened(session, cameOnline));
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
			if (open.GiveWay(now) is SessionActionResult.Active) return LoginFailure.AlreadyOnline;

			CloseCore(open, open.State.Record.Value.Ended!.Value.Reason);
			return null;
		}

		if (stored is null || stored.Equals(session.State)) return null;

		if (stored.GiveWay(now) is SessionActionResult.Active) return LoginFailure.AlreadyOnline;

		if (stored.Record.Value.Ended is not null) _ended[stored.Record.Id] = 0;
		return null;
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
			         .Where(connection => connection.IsOpen && connection.State.Record.Id != except &&
			                              (only is null || connection.State.Record.Id == only))
			         .OrderByDescending(connection => connection.Type).ToList())
			Close(connection, reason);
	}

	/// <summary>Closes the tourney sessions of an account that no longer holds the privilege they need.</summary>
	internal void CloseDisallowed(UserRecord record)
	{
		if (_online.GetValueOrDefault(record) is not { } online) return;
		if (online.User.EffectivePrivileges(_context.Clock.GetUtcNow()).Allows(UserPrivileges.Tournament)) return;

		foreach (var connection in online.Tourneys.Where(connection => connection.IsOpen).ToList())
			Close(connection, SessionEndReason.Revoked);
	}

	internal void Add(UserSessions user)
	{
		_online[user.Record] = user;
		_users.Keep(user.User);
	}

	internal bool Remove(UserSessions user)
	{
		_users.Release(user.User);
		return _online.TryRemove(new KeyValuePair<UserRecord, UserSessions>(user.Record, user));
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

		connection.End(_context.Clock.GetUtcNow(), reason);
		if (!connection.IsServerOwned) _ended[connection.State.Record.Id] = 0;
		_activity.Forget(connection.State.Record.Id);
		connection.IsOpen = false;
		Unindex(connection);
		connection.Siblings.Remove(connection);
		if (connection is StableSession client)
			client.ReleaseSpectators();

		var wentOffline = !connection.Siblings.Connections.Any();
		if (wentOffline) Remove(connection.Siblings);

		if (!connection.IsServerOwned)
		{
			SessionMetrics.Record(connection.Type, SessionCount.Closed);
			if (reason is SessionEndReason.TimedOut) SessionMetrics.Record(connection.Type, SessionCount.Timeout);
			else if (reason is SessionEndReason.ConnectionLost)
				SessionMetrics.Record(connection.Type, SessionCount.Errors);
		}

		_context.Events.Publish(new SessionClosed(connection, reason, wentOffline));
	}

	internal static long NewSessionId()
	{
		return RandomNumberGenerator.GetInt32(int.MaxValue) * 4194304L + RandomNumberGenerator.GetInt32(4194304) + 1;
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