using System.Net;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Authentication;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Moderation;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Testing;

internal sealed class InMemoryUserRepository : IUserRepository
{
	private readonly Dictionary<int, UserRecord> _users = [];
	private int _nextId = 2;

	/// <summary>Gets or sets the failure the next updates throw, or <see langword="null" /> to accept them.</summary>
	public Exception? UpdateFailure { get; set; }

	public Task<UserRecord> CreateAsync(UserData data, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_users.Values.Any(user => user.Value.DeletedAt is null &&
		                              user.Value.NormalizedName == data.NormalizedName))
			throw new AlreadyExistsException(data.Name);

		return Task.FromResult(Seed(data));
	}

	public Task<UserRecord> ReplaceAsync(int id, UserData data, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_users.Values.Any(user => user.Id != id && user.Value.DeletedAt is null &&
		                              user.Value.NormalizedName == data.NormalizedName))
			throw new AlreadyExistsException(data.Name);

		return Task.FromResult(Seed(data, id));
	}

	public Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (UpdateFailure is { } failure) throw failure;
		if (_users.TryGetValue(user.Id, out var stored) && !ReferenceEquals(stored, user))
		{
			stored.Value.Country = user.Value.Country;
			stored.Value.Privileges = user.Value.Privileges;
			stored.Value.SilencedUntil = user.Value.SilencedUntil;
			if (user.Value.DeletedAt is not null) stored.Value.DeletedAt = user.Value.DeletedAt;
		}
		else
		{
			_users[user.Id] = user;
		}

		return Task.CompletedTask;
	}

	public Task DiscardAsync(UserRecord user, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_users.Remove(user.Id);
		return Task.CompletedTask;
	}

	public Task<bool> RenameAsync(UserRecord user, string name, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var normalized = UserData.NormalizeName(name);
		if (_users.Values.Any(other => other.Id != user.Id && other.Value.DeletedAt is null &&
		                               other.Value.NormalizedName == normalized))
			return Task.FromResult(false);

		user.Value.Name = name;
		return Task.FromResult(true);
	}

	public ValueTask<UserRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult(_users.GetValueOrDefault(id));
	}

	public ValueTask<UserRecord?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var normalized = UserData.NormalizeName(name);
		return ValueTask.FromResult(_users.Values.FirstOrDefault(user =>
			user.Value.NormalizedName == normalized));
	}

	public Task<Page<UserRecord>> ListAsync(UserQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(new Page<UserRecord>([], false));
	}

	public UserRecord Seed(UserData data, int? id = null)
	{
		var userId = id ?? _nextId++;
		_nextId = Math.Max(_nextId, userId + 1);
		var user = new UserRecord { Id = userId, Value = data };
		_users[user.Id] = user;
		return user;
	}
}

internal sealed class InMemoryCredentialRepository : ICredentialRepository
{
	private readonly Dictionary<int, Md5> _passwords = [];

	public Task<bool> VerifyAsync(Credentials credentials, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(_passwords.TryGetValue(credentials.User.Id, out var password) &&
		                       password == credentials.PasswordHash);
	}

	public Task CreateOrUpdateAsync(Credentials credentials, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_passwords[credentials.User.Id] = credentials.PasswordHash;
		return Task.CompletedTask;
	}

	public Task DeleteAsync(UserRecord user, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_passwords.Remove(user.Id);
		return Task.CompletedTask;
	}
}

internal sealed class InMemorySessionRepository : ISessionRepository
{
	private readonly List<SessionRecord> _entries = [];

	public IReadOnlyList<SessionRecord> Entries => _entries;

	/// <summary>Gets or sets the failure the next recordings throw, or <see langword="null" /> to accept them.</summary>
	public Exception? RecordFailure { get; set; }

	public Task CreateOrUpdateAsync(SessionRecord session, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var index = _entries.FindIndex(entry => entry.Id == session.Id);
		if (index >= 0)
		{
			_entries[index] = session;
			return Task.CompletedTask;
		}

		if (RecordFailure is { } failure) throw failure;
		_entries.Add(session);
		return Task.CompletedTask;
	}

	public ValueTask<Page<SessionRecord>> ListAsync(SessionQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var entries = _entries.Where(entry => entry.Value.User.Equals(query.User) &&
		                                      (query.Types is null || query.Types.Contains(entry.Value.Type)) &&
		                                      query.State switch
		                                      {
			                                      SessionState.Active => IsActive(entry, query.Now),
			                                      SessionState.Ended => !IsActive(entry, query.Now),
			                                      _ => true
		                                      }).Reverse().ToList();
		return ValueTask.FromResult(new Page<SessionRecord>(entries.Skip(page.Offset).Take(page.Limit).ToList(),
			page.Offset + page.Limit < entries.Count));
	}

	public Task<SessionRecord?> GetAsync(UserRecord user, long id, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_entries.FirstOrDefault(entry => entry.Id == id && entry.Value.User.Equals(user)));
	}

	public Task<StoredSession?> FindByTokenAsync(SessionType type, Sha256 tokenHash, DateTimeOffset now,
		CancellationToken cancellationToken = default)
	{
		var found = _entries.FirstOrDefault(entry => entry.Value.Type == type && entry.Value.End is null &&
		                                             entry.Value.TokenHash == tokenHash &&
		                                             (entry.Value.ExpiresAt is null || entry.Value.ExpiresAt > now));
		return Task.FromResult(found is null
			? null
			: new StoredSession(found.Id, found.Value.User.Id, found.Value.Type, found.Value.Fingerprint.Ip,
				found.Value.Fingerprint.ClientInfo, found.Value.StartedAt, found.Value.ExpiresAt));
	}

	public Task EndAsync(UserRecord user, long? only, long? except, IReadOnlyList<SessionType> types,
		DateTimeOffset at, SessionEndReason reason, CancellationToken cancellationToken = default)
	{
		for (var index = 0; index < _entries.Count; index++)
		{
			var entry = _entries[index];
			if (!entry.Value.User.Equals(user) || !types.Contains(entry.Value.Type) ||
			    (only is not null && entry.Id != only) || entry.Id == except)
				continue;

			_entries[index] = With(entry, data => data with { End = data.End ?? new SessionEnd(at, reason) });
		}

		return Task.CompletedTask;
	}

	public Task<int> EndOpenAsync(IReadOnlyList<SessionType> types, SessionEndReason reason,
		CancellationToken cancellationToken = default)
	{
		var ended = 0;
		for (var index = 0; index < _entries.Count; index++)
		{
			var entry = _entries[index];
			if (entry.Value.End is not null || !types.Contains(entry.Value.Type)) continue;

			_entries[index] = With(entry,
				data => data with { End = new SessionEnd(data.LastActiveAt, reason) });
			ended++;
		}

		return Task.FromResult(ended);
	}

	public Task WriteActivityAsync(IReadOnlyCollection<SessionActivity> activity,
		CancellationToken cancellationToken = default)
	{
		foreach (var item in activity)
		{
			var index = _entries.FindIndex(entry => entry.Id == item.Id && entry.Value.End is null);
			if (index < 0) continue;

			_entries[index] = With(_entries[index],
				data => data with { LastActiveAt = item.LastActiveAt, ExpiresAt = item.ExpiresAt });
		}

		return Task.CompletedTask;
	}

	public Task<int> DeleteEndedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
	{
		var deleted = _entries.RemoveAll(entry => entry.Value.End?.At < cutoff ||
		                                          (entry.Value.End is null && entry.Value.ExpiresAt < cutoff));
		return Task.FromResult(deleted);
	}

	/// <summary>Drops a stored session, as if its record had not reached the store yet.</summary>
	public void Forget(long id)
	{
		_entries.RemoveAll(entry => entry.Id == id);
	}

	/// <summary>Reads the stored expiry of a session's token.</summary>
	/// <param name="id">The session.</param>
	/// <returns>When the token expires, or <see langword="null" /> if it has no expiry.</returns>
	public DateTimeOffset? ExpiryOf(long id)
	{
		return _entries.FirstOrDefault(entry => entry.Id == id)?.Value.ExpiresAt;
	}

	private static bool IsActive(SessionRecord entry, DateTimeOffset now)
	{
		return entry.Value.End is null && (entry.Value.ExpiresAt is not { } expiresAt || expiresAt > now);
	}

	private static SessionRecord With(SessionRecord session, Func<SessionData, SessionData> change)
	{
		return new SessionRecord { Id = session.Id, Value = change(session.Value) };
	}
}

internal sealed class InMemoryConnectionRepository : IConnectionRepository
{
	private readonly List<ConnectionRecord> _entries = [];
	private long _nextId = 1;

	public IReadOnlyList<ConnectionRecord> Entries => _entries;

	public Task OpenAsync(long sessionId, IPAddress ip, string? clientInfo, DateTimeOffset at, TimeSpan reopenWithin,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var lastIndex = _entries.FindLastIndex(entry => entry.Value.SessionId == sessionId);
		if (lastIndex >= 0 && _entries[lastIndex].Value is { ClosedAt: { } closedAt } last &&
		    closedAt >= at - reopenWithin && last.Ip.Equals(ip))
		{
			_entries[lastIndex] = With(_entries[lastIndex], data => data with
			{
				ClosedAt = null, CloseReason = null, LastActiveAt = at
			});
			return Task.CompletedTask;
		}

		_entries.Add(new ConnectionRecord
		{
			Id = _nextId++, Value = new ConnectionData(sessionId, ip, clientInfo, at, at)
		});
		return Task.CompletedTask;
	}

	public Task CloseAsync(long sessionId, DateTimeOffset lastActiveAt, DateTimeOffset closedAt,
		SessionEndReason reason, CancellationToken cancellationToken = default)
	{
		for (var index = 0; index < _entries.Count; index++)
			if (_entries[index].Value is { ClosedAt: null } data && data.SessionId == sessionId)
				_entries[index] = With(_entries[index], value => value with
				{
					LastActiveAt = lastActiveAt, ClosedAt = closedAt, CloseReason = reason
				});

		return Task.CompletedTask;
	}

	public Task WriteActivityAsync(IReadOnlyCollection<ConnectionActivity> activity,
		CancellationToken cancellationToken = default)
	{
		foreach (var item in activity)
			for (var index = 0; index < _entries.Count; index++)
				if (_entries[index].Value is { ClosedAt: null } data && data.SessionId == item.SessionId)
					_entries[index] = With(_entries[index], value => value with { LastActiveAt = item.LastActiveAt });

		return Task.CompletedTask;
	}

	public ValueTask<Page<ConnectionRecord>> ListAsync(long sessionId, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var entries = _entries.Where(entry => entry.Value.SessionId == sessionId).Reverse().ToList();
		return ValueTask.FromResult(new Page<ConnectionRecord>(entries.Skip(page.Offset).Take(page.Limit).ToList(),
			page.Offset + page.Limit < entries.Count));
	}

	public Task<int> CloseOpenAsync(SessionEndReason reason, CancellationToken cancellationToken = default)
	{
		var closed = 0;
		for (var index = 0; index < _entries.Count; index++)
		{
			if (_entries[index].Value.ClosedAt is not null) continue;

			_entries[index] = With(_entries[index], value => value with
			{
				ClosedAt = value.LastActiveAt, CloseReason = reason
			});
			closed++;
		}

		return Task.FromResult(closed);
	}

	private static ConnectionRecord With(ConnectionRecord record, Func<ConnectionData, ConnectionData> change)
	{
		return new ConnectionRecord { Id = record.Id, Value = change(record.Value) };
	}
}

internal sealed class InMemoryRelationshipRepository : IRelationshipRepository
{
	private readonly List<Relationship> _relationships = [];

	public Task CreateOrUpdateAsync(Relationship relationship, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_relationships.RemoveAll(existing => existing.Actor.Equals(relationship.Actor) &&
		                                     existing.Target.Equals(relationship.Target));
		_relationships.Add(relationship);
		return Task.CompletedTask;
	}

	public Task DeleteAsync(Relationship relationship, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_relationships.Remove(relationship);
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<Relationship>> ListAsync(UserRecord actor,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult<IReadOnlyList<Relationship>>(
			_relationships.Where(relationship => relationship.Actor.Equals(actor)).ToList());
	}
}

internal sealed class InMemoryModerationLogRepository : IModerationLogRepository
{
	private readonly List<ModerationLog> _entries = [];
	private readonly List<ModerationData> _values = [];
	private long _nextId = 1;

	public IReadOnlyList<ModerationData> Entries => _values;

	/// <summary>Gets or sets the failure the next appends throw, or <see langword="null" /> to accept them.</summary>
	public Exception? AppendFailure { get; set; }

	public Task AppendAsync(ModerationData entry, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (AppendFailure is { } failure) throw failure;
		_entries.Add(new ModerationLog { Id = _nextId++, Value = entry });
		_values.Add(entry);
		return Task.CompletedTask;
	}

	public ValueTask<Page<ModerationLog>> ListAsync(UserRecord target, Interval<DateTimeOffset>? within,
		PageRequest page, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var entries = _entries.Where(entry => entry.Value.Target.Equals(target) &&
		                                      (within is null || within.Value.Contains(entry.Value.Timestamp)))
			.Reverse().ToList();
		return ValueTask.FromResult(new Page<ModerationLog>(entries.Skip(page.Offset).Take(page.Limit).ToList(),
			page.Offset + page.Limit < entries.Count));
	}
}

internal sealed class InMemoryMatchRepository(InMemoryMatchEventRepository events) : IMatchRepository
{
	private readonly Dictionary<int, MatchRecord> _matches = [];
	private int _nextId = 1;

	public Task<MatchRecord> CreateAsync(MatchData data, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var match = new MatchRecord { Id = _nextId++, Value = data };
		_matches[match.Id] = match;
		return Task.FromResult(match);
	}

	public Task UpdateAsync(MatchRecord match, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_matches[match.Id] = match;
		return Task.CompletedTask;
	}

	public ValueTask<MatchRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult(_matches.GetValueOrDefault(id));
	}

	public Task<Page<MatchRecord>> ListAsync(MatchQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var matches = _matches.Values
			.Where(match => query.Ended is not { } ended || match.Value.EndedAt is not null == ended)
			.Where(match => query.Private is not { } isPrivate || match.Value.IsPrivate == isPrivate)
			.Where(match => query.VisibleTo is not { } viewer || IsVisibleTo(match, viewer))
			.OrderBy(match => match.Id)
			.ToList();
		return Task.FromResult(new Page<MatchRecord>(
			matches.Skip(page.Offset).Take(page.Limit).ToList(), page.Offset + page.Limit < matches.Count));
	}

	public Task<bool> IsVisibleToAsync(MatchRecord match, UserRecord viewer,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(IsVisibleTo(match, viewer.Id));
	}

	private bool IsVisibleTo(MatchRecord match, int viewer)
	{
		if (!match.Value.IsPrivate || match.Value.Creator.Id == viewer) return true;
		var history = events.EventsOf(match).ToList();
		return history.Any(matchEvent =>
			       matchEvent.Type == MatchEventType.PlayerJoined && matchEvent.Target?.Id == viewer)
		       || history.LastOrDefault(matchEvent => matchEvent.Target?.Id == viewer
		                                              && matchEvent.Type is MatchEventType.RefAdded
			                                              or MatchEventType.RefRemoved)?.Type ==
		       MatchEventType.RefAdded;
	}
}

internal sealed class InMemoryRoundRepository : IRoundRepository
{
	private readonly List<Round> _rounds = [];

	public Task CreateOrUpdateAsync(Round round, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_rounds.RemoveAll(existing => existing.Match.Equals(round.Match) && existing.Index == round.Index);
		_rounds.Add(round);
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<Round>> ListAsync(MatchRecord match, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult<IReadOnlyList<Round>>(_rounds.Where(round => round.Match.Equals(match)).ToList());
	}
}

internal sealed class InMemoryMatchEventRepository : IMatchEventRepository
{
	private readonly List<MatchEvent> _events = [];

	public Task RecordAsync(MatchEvent matchEvent, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_events.Add(matchEvent);
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<MatchEvent>> ListAsync(MatchRecord match, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult<IReadOnlyList<MatchEvent>>(_events.Where(matchEvent =>
			matchEvent.Match.Equals(match)).ToList());
	}

	public IEnumerable<MatchEvent> EventsOf(MatchRecord match)
	{
		return _events.Where(matchEvent => matchEvent.Match.Equals(match));
	}
}

internal sealed class InMemorySettingsRepository : ISettingsRepository
{
	public ServerSettings Current { get; private set; } = new();

	public ValueTask<ServerSettings> GetAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult(Current);
	}

	public Task CreateOrUpdateAsync(ServerSettings settings, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Current = settings;
		return Task.CompletedTask;
	}
}