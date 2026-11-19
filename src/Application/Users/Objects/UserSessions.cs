using System.Collections.Concurrent;
using System.Collections.Immutable;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Authentication;
using Basil.Domain.Moderation;
using Basil.Domain.Users;
using Basil.Shared.Structures.Data;
using Session = Basil.Application.Users.Objects.Sessions.Session;

namespace Basil.Application.Users.Objects;

public sealed class UserSessions
{
	private readonly ConcurrentDictionary<SessionType, ConcurrentSet<Session>> _connections = new();
	private readonly User _user;

	internal UserSessions(User user)
	{
		_user = user;
	}

	public IReadOnlySet<Session> this[SessionType type] =>
		_connections.TryGetValue(type, out var set) ? set : ImmutableHashSet<Session>.Empty;

	public IEnumerable<Session> Connections => _connections.SelectMany(pair => pair.Value);

	public StableSession? Osu =>
		(StableSession?)this[SessionType.Stable].SingleOrDefault(connection => !connection.IsServerOwned);

	public IrcSession? Irc => Ircs.FirstOrDefault();

	public IEnumerable<IrcSession> Ircs => this[SessionType.Irc].Cast<IrcSession>();

	public IEnumerable<TourneySession> Tourneys => this[SessionType.StableTourney].Cast<TourneySession>();

	internal void Add(Session connection)
	{
		using var scope = _user.Enter();
		var set = _connections.GetOrAdd(connection.Type, _ => []);
		set.Add(connection);
	}

	internal bool Remove(Session connection)
	{
		using var scope = _user.Enter();
		return _connections.TryGetValue(connection.Type, out var set) && set.Remove(connection);
	}

	internal async Task<Page<Domain.Authentication.Session>> ListAsync(Session viewer, PageRequest page,
		SessionFilter state, IReadOnlyList<SessionType>? types, CancellationToken ct)
	{
		var now = _user.Context.Clock.GetUtcNow();
		if (!viewer.Record.Equals(_user.Record))
			await _user.Context.ModerationLog.AppendAsync(new ModerationLogData
			{
				Action = ModerationAction.ViewLogins,
				Actor = viewer.Record,
				Target = _user.Record,
				Timestamp = now
			});

		return await _user.Context.Sessions.ListAsync(new SessionQuery(_user.Record, state, types), page,
			ct);
	}

	internal async Task<(UserOutcome Outcome, Domain.Authentication.Session? Session)> GetAsync(Session viewer,
		long id, CancellationToken ct)
	{
		var session = await _user.Context.Sessions.GetAsync(_user.Record, id, ct);
		if (session is null) return (UserOutcome.NotFound, null);

		if (!viewer.Record.Equals(_user.Record))
			await _user.Context.ModerationLog.AppendAsync(new ModerationLogData
			{
				Action = ModerationAction.ViewLogins,
				Actor = viewer.Record,
				Target = _user.Record,
				Timestamp = _user.Context.Clock.GetUtcNow()
			});

		return (UserOutcome.Ok, session);
	}

	internal async Task<UserOutcome> CloseAsync(long id)
	{
		if (_user.Record.IsBasilBot()) return UserOutcome.SystemAccount;
		// A session whose client is online exists even before its record reaches the store.
		if (!Connections.Any(connection => connection.State.Id == id) &&
		    await _user.Context.Sessions.GetAsync(_user.Record, id) is not { Ended: null })
			return UserOutcome.NotFound;

		await CloseAsync(id, null, SessionEndReason.Revoked);
		return UserOutcome.Ok;
	}

	internal async Task<UserOutcome> CloseAllAsync(Session actor, bool keepCurrent)
	{
		if (_user.Record.IsBasilBot()) return UserOutcome.SystemAccount;

		var isSelf = actor.Record.Equals(_user.Record);
		long? keep = isSelf && keepCurrent && actor is ApiSession api ? api.State.Id : null;
		// The sessions live in another module's database, so no transaction covers both: log first, so an action is never unrecorded.
		if (!isSelf)
			await _user.Context.ModerationLog.AppendAsync(new ModerationLogData
			{
				Action = ModerationAction.CloseSessions,
				Actor = actor.Record,
				Target = _user.Record,
				Timestamp = _user.Context.Clock.GetUtcNow()
			});

		await CloseAsync(null, keep, SessionEndReason.Revoked);

		return UserOutcome.Ok;
	}

	internal async Task CloseAsync(long? only, long? except, SessionEndReason reason)
	{
		var registry = _user.Context.Registry;
		var now = _user.Context.Clock.GetUtcNow();
		// A failed write leaves the revocation in place: the sessions stay unresumable.
		var revocation = registry.BeginRevoke(_user.Record, only, except, now);
		registry.CloseAll(_user.Record, reason, except, only);
		await _user.Context.Sessions.EndAsync(_user.Record, only, except, reason, now);
		registry.EndRevoke(_user.Record, only, revocation);
	}
}