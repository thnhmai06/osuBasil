using System.Collections.Concurrent;
using System.Collections.Immutable;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Moderation;

using Basil.Domain.Users;
using Basil.Shared.Structures.Data;

namespace Basil.Application.Users.Objects;

public sealed class UserSessions
{
	private const int MaxLabelLength = 64;

	private static readonly SessionType[] ClientTypes =
		[SessionType.Stable, SessionType.StableTourney, SessionType.Irc, SessionType.Api];

	private readonly ConcurrentDictionary<SessionType, ConcurrentSet<Connection>> _connections = new();
	private readonly User _user;

	internal UserSessions(User user)
	{
		_user = user;
	}

	public IReadOnlySet<Connection> this[SessionType type] =>
		_connections.TryGetValue(type, out var set) ? set : ImmutableHashSet<Connection>.Empty;

	public IEnumerable<Connection> Connections => _connections.SelectMany(pair => pair.Value);

	public StableConnection? Osu =>
		(StableConnection?)this[SessionType.Stable].SingleOrDefault(connection => !connection.IsServerOwned);

	public IrcConnection? Irc => Ircs.FirstOrDefault();

	public IEnumerable<IrcConnection> Ircs => this[SessionType.Irc].Cast<IrcConnection>();

	public IEnumerable<TourneyConnection> Tourneys => this[SessionType.StableTourney].Cast<TourneyConnection>();

	internal void Add(Connection connection)
	{
		using var scope = _user.Enter();
		var set = _connections.GetOrAdd(connection.Type, _ => []);
		set.Add(connection);
	}

	internal bool Remove(Connection connection)
	{
		using var scope = _user.Enter();
		return _connections.TryGetValue(connection.Type, out var set) && set.Remove(connection);
	}

	internal async Task<Page<SessionRecord>> ListAsync(Connection viewer, PageRequest page, SessionState state,
		IReadOnlyList<SessionType>? types, CancellationToken ct)
	{
		var now = _user.Context.Clock.GetUtcNow();
		if (!viewer.Record.Equals(_user.Record))
			await _user.Context.ModerationLog.AppendAsync(new ModerationData
			{
				Action = ModerationAction.ViewLogins,
				Actor = viewer.Record,
				Target = _user.Record,
				Timestamp = now
			});

		return await _user.Context.Sessions.ListAsync(new SessionQuery(_user.Record, state, types, now), page,
			ct);
	}

	internal async Task<(UserOutcome Outcome, SessionRecord? Session)> GetAsync(Connection viewer, long id,
		CancellationToken ct)
	{
		var session = await _user.Context.Sessions.GetAsync(_user.Record, id, ct);
		if (session is null) return (UserOutcome.NotFound, null);

		if (!viewer.Record.Equals(_user.Record))
			await _user.Context.ModerationLog.AppendAsync(new ModerationData
			{
				Action = ModerationAction.ViewLogins,
				Actor = viewer.Record,
				Target = _user.Record,
				Timestamp = _user.Context.Clock.GetUtcNow()
			});

		return (UserOutcome.Ok, session);
	}

	internal async Task<(UserOutcome Outcome, Page<ConnectionRecord>? Connections)> ListConnectionsAsync(
		Connection viewer, long id, PageRequest page, CancellationToken ct)
	{
		var (outcome, _) = await GetAsync(viewer, id, ct);
		if (outcome is not UserOutcome.Ok) return (outcome, null);

		return (UserOutcome.Ok, await _user.Context.Connections.ListAsync(id, page, ct));
	}

	internal async Task<UserOutcome> CloseAsync(long id)
	{
		if (_user.Record.IsBasilBot()) return UserOutcome.SystemAccount;
		// A session whose client is online exists even before its record reaches the store.
		if (!Connections.Any(connection => connection.Session.Id == id) &&
		    await _user.Context.Sessions.GetAsync(_user.Record, id) is not { Value.End: null })
			return UserOutcome.NotFound;

		await CloseAsync(id, null, SessionEndReason.Revoked);
		return UserOutcome.Ok;
	}

	internal async Task<UserOutcome> CloseAllAsync(Connection actor, bool keepCurrent)
	{
		if (_user.Record.IsBasilBot()) return UserOutcome.SystemAccount;

		var isSelf = actor.Record.Equals(_user.Record);
		long? keep = isSelf && keepCurrent && actor is ApiConnection api ? api.Session.Id : null;
		// The sessions live in another module's database, so no transaction covers both: log first, so an action is never unrecorded.
		if (!isSelf)
			await _user.Context.ModerationLog.AppendAsync(new ModerationData
			{
				Action = ModerationAction.CloseSessions,
				Actor = actor.Record,
				Target = _user.Record,
				Timestamp = _user.Context.Clock.GetUtcNow()
			});

		await CloseAsync(null, keep, SessionEndReason.Revoked);

		return UserOutcome.Ok;
	}

	internal async Task<(UserOutcome Outcome, string? Key, SessionRecord? Session)> OpenIrcAsync(Connection actor,
		string label)
	{
		if (_user.Record.Value.DeletedAt is not null) return (UserOutcome.Deleted, null, null);

		label = label.Trim();
		if (label.Length is 0 or > MaxLabelLength) return (UserOutcome.InvalidLabel, null, null);

		var key = UserRegistry.NewToken();
		var now = _user.Context.Clock.GetUtcNow();
		var session = new SessionRecord
		{
			Id = UserRegistry.NewSessionId(),
			Value = new SessionData(_user.Record, SessionType.Irc, now,
				new SessionFingerprint(actor.Session.Value.Fingerprint.Ip, ClientInfo: label),
				UserRegistry.HashToken(key), null, now)
		};
		await _user.Context.Sessions.CreateOrUpdateAsync(session);
		return (UserOutcome.Ok, key, session);
	}

	internal async Task CloseAsync(long? only, long? except, SessionEndReason reason)
	{
		_user.Context.Registry.CloseAll(_user.Record, reason, except, only);
		await _user.Context.Sessions.EndAsync(_user.Record, only, except, ClientTypes,
			_user.Context.Clock.GetUtcNow(), reason);
	}
}