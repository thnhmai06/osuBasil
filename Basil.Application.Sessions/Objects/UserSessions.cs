using System.Collections.Concurrent;
using System.Collections.Immutable;
using Basil.Application.Sessions.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects;
using Basil.Domain.Sessions;
using Basil.Domain.Moderation;
using Basil.Domain.Users;
using Basil.Shared.Structures.Data;
using Session = Basil.Application.Sessions.Objects.Session;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Application.Sessions.Objects;

/// <summary>The sessions one account has open, kept together with its account object.</summary>
public sealed class UserSessions
{
	private readonly ConcurrentDictionary<SessionType, ConcurrentSet<Session>> _connections = new();
	private readonly SessionContext _context;

	internal UserSessions(User user, SessionContext context)
	{
		User = user;
		_context = context;
		Credentials = new UserCredentials(this, context);
	}

	public User User { get; }

	public UserRecord Record => User.Record;

	public UserCredentials Credentials { get; }

	public UserPrivileges EffectivePrivileges(DateTimeOffset now)
	{
		return User.EffectivePrivileges(now);
	}

	/// <summary>Whether this is the set the registry holds for an online account.</summary>
	public bool IsOnline => _context.Registry.OnlineNow.TryGetValue(Record, out var online) && ReferenceEquals(online, this);

	public IReadOnlySet<Session> this[SessionType type] =>
		_connections.TryGetValue(type, out var set) ? set : ImmutableHashSet<Session>.Empty;

	public IEnumerable<Session> Connections => _connections.SelectMany(pair => pair.Value);

	public StableSession? Stable =>
		(StableSession?)this[SessionType.Stable].SingleOrDefault(connection => !connection.IsServerOwned);

	public IrcSession? Irc => Ircs.FirstOrDefault();

	public IEnumerable<IrcSession> Ircs => this[SessionType.Irc].Cast<IrcSession>();

	public IEnumerable<TourneySession> Tourneys => this[SessionType.StableTourney].Cast<TourneySession>();

	internal void Add(Session connection)
	{
		var set = _connections.GetOrAdd(connection.Type, _ => []);
		set.Add(connection);
	}

	internal bool Remove(Session connection)
	{
		return _connections.TryGetValue(connection.Type, out var set) && set.Remove(connection);
	}

	internal async Task<Page<SessionRecord>> ListAsync(Session viewer, PageRequest page,
		SessionFilter state, IReadOnlyList<SessionType>? types, CancellationToken ct)
	{
		var now = _context.Clock.GetUtcNow();
		if (!viewer.Record.Equals(Record))
			await _context.ModerationLog.AppendAsync(new ModerationLogData
			{
				Action = ModerationAction.ViewLogins,
				Actor = viewer.Record,
				Target = Record,
				Timestamp = now
			});

		return await _context.Sessions.ListAsync(new SessionQuery(Record, state, types), page,
			ct);
	}

	internal async Task<(UserActionResult Outcome, SessionRecord? Session)> GetAsync(Session viewer,
		long id, CancellationToken ct)
	{
		var session = await _context.Sessions.GetAsync(Record, id, ct);
		if (session is null) return (UserActionResult.NotFound, null);

		if (!viewer.Record.Equals(Record))
			await _context.ModerationLog.AppendAsync(new ModerationLogData
			{
				Action = ModerationAction.ViewLogins,
				Actor = viewer.Record,
				Target = Record,
				Timestamp = _context.Clock.GetUtcNow()
			});

		return (UserActionResult.Ok, session);
	}

	internal async Task<UserActionResult> CloseAsync(long id)
	{
		if (Record.Id == BasilBot.Id) return UserActionResult.SystemAccount;
		// A session whose client is online exists even before its record reaches the store.
		if (!Connections.Any(connection => connection.State.Record.Id == id) &&
		    await _context.Sessions.GetAsync(Record, id) is not { Value.Ended: null })
			return UserActionResult.NotFound;

		await CloseAsync(id, null, SessionEndReason.Revoked);
		return UserActionResult.Ok;
	}

	internal async Task<UserActionResult> CloseAllAsync(Session actor, bool keepCurrent)
	{
		if (Record.Id == BasilBot.Id) return UserActionResult.SystemAccount;

		var isSelf = actor.Record.Equals(Record);
		long? keep = isSelf && keepCurrent && actor is ApiSession api ? api.State.Record.Id : null;
		// The sessions live in another module's database, so no transaction covers both: log first, so an action is never unrecorded.
		if (!isSelf)
			await _context.ModerationLog.AppendAsync(new ModerationLogData
			{
				Action = ModerationAction.CloseSessions,
				Actor = actor.Record,
				Target = Record,
				Timestamp = _context.Clock.GetUtcNow()
			});

		await CloseAsync(null, keep, SessionEndReason.Revoked);

		return UserActionResult.Ok;
	}

	internal async Task CloseAsync(long? only, long? except, SessionEndReason reason)
	{
		var registry = _context.Registry;
		var now = _context.Clock.GetUtcNow();
		// A failed write leaves the revocation in place: the sessions stay unresumable.
		var revocation = registry.BeginRevoke(Record, only, except, now);
		registry.CloseAll(Record, reason, except, only);
		await _context.Sessions.EndAsync(Record, only, except, reason, now);
		registry.EndRevoke(Record, only, revocation);
	}
}