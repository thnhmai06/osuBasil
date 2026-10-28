using Basil.Application.Auth.Persistence;
using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Moderation;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Testing;

internal sealed class InMemoryUserRepository : IUserRepository
{
	private readonly Dictionary<int, UserRecord> _users = [];
	private int _nextId = 2;

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
		_users[user.Id] = user;
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
		return Task.FromResult(new Page<UserRecord>([], 0));
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

internal sealed class InMemoryRestrictionRepository : IRestrictionRepository
{
	private readonly Dictionary<int, List<UserRestriction>> _restrictions = [];
	private int _nextId = 1;

	public Task<UserRestriction> CreateAsync(RestrictionData data, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var restriction = new UserRestriction { Id = _nextId++, Value = data };
		GetFor(data.User).Add(restriction);
		return Task.FromResult(restriction);
	}

	public Task UpdateAsync(UserRestriction restriction, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.CompletedTask;
	}

	public Task DeleteAsync(UserRestriction restriction, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		GetFor(restriction.Value.User).RemoveAll(item => item.Id == restriction.Id);
		return Task.CompletedTask;
	}

	public ValueTask<UserRestriction?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult(_restrictions.Values.SelectMany(items => items)
			.FirstOrDefault(restriction => restriction.Id == id));
	}

	public Task<IReadOnlyList<UserRestriction>> ListAsync(UserRecord user,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult<IReadOnlyList<UserRestriction>>(GetFor(user));
	}

	public UserRestriction Seed(UserRecord user, UserPermissions permissions, DateTimeOffset startsAt,
		DateTimeOffset? endsAt = null)
	{
		var restriction = new UserRestriction
		{
			Id = _nextId++,
			Value = new RestrictionData
			{
				User = user,
				Permissions = permissions,
				StartsAt = startsAt,
				EndsAt = endsAt
			}
		};
		GetFor(user).Add(restriction);
		return restriction;
	}

	private List<UserRestriction> GetFor(UserRecord user)
	{
		return _restrictions.GetValueOrDefault(user.Id) ?? AddList(user.Id);
	}

	private List<UserRestriction> AddList(int userId)
	{
		var list = new List<UserRestriction>();
		_restrictions.Add(userId, list);
		return list;
	}
}

internal sealed class InMemorySessionRepository : ISessionRepository
{
	private readonly List<SessionEntry> _entries = [];
	private readonly Dictionary<Guid, (byte[]? Hash, DateTimeOffset? ExpiresAt)> _tokens = [];

	public IReadOnlyList<SessionEntry> Entries => _entries;

	public Task RecordAsync(Session login, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_entries.Add(ToEntry(login));
		_tokens[login.Id] = (login.TokenHash, login.ExpiresAt);
		return Task.CompletedTask;
	}

	public Task UpdateAsync(Session session)
	{
		var index = _entries.FindIndex(entry => entry.Id == session.Id);
		if (index >= 0)
		{
			_entries[index] = ToEntry(session);
			_tokens[session.Id] = (session.TokenHash, session.ExpiresAt);
		}

		return Task.CompletedTask;
	}

	public ValueTask<Page<SessionEntry>> ListAsync(UserRecord user, SessionFilter filter, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var entries = _entries.Where(entry => entry.User.Equals(user) &&
		                                      (filter.Types is null || filter.Types.Contains(entry.Type)) &&
		                                      filter.State switch
		                                      {
			                                      SessionState.Active => IsActive(entry, filter.Now),
			                                      SessionState.Ended => !IsActive(entry, filter.Now),
			                                      _ => true
		                                      }).Reverse().ToList();
		return ValueTask.FromResult(new Page<SessionEntry>(entries.Skip(page.Offset).Take(page.Limit).ToList(),
			entries.Count));
	}

	public Task<SessionEntry?> GetAsync(UserRecord user, Guid id, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_entries.FirstOrDefault(entry => entry.Id == id && entry.User.Equals(user)));
	}

	public Task<StoredSession?> FindByTokenAsync(ConnectionType type, byte[] tokenHash, DateTimeOffset now,
		CancellationToken cancellationToken = default)
	{
		var found = _entries.FirstOrDefault(entry => entry.Type == type && entry.EndedAt is null &&
		                                             _tokens.GetValueOrDefault(entry.Id) is { Hash: { } hash } token &&
		                                             hash.AsSpan().SequenceEqual(tokenHash) &&
		                                             (token.ExpiresAt is null || token.ExpiresAt > now));
		return Task.FromResult(found is null
			? null
			: new StoredSession(found.Id, found.User.Id, found.Type, found.Ip, found.ClientInfo, found.StartedAt,
				_tokens[found.Id].ExpiresAt));
	}

	public Task EndAsync(UserRecord user, Guid? only, Guid? except, IReadOnlyList<ConnectionType> types,
		DateTimeOffset at, SessionEndReason reason, bool requireReauthentication,
		CancellationToken cancellationToken = default)
	{
		for (var index = 0; index < _entries.Count; index++)
		{
			var entry = _entries[index];
			if (!entry.User.Equals(user) || !types.Contains(entry.Type) || (only is not null && entry.Id != only) ||
			    entry.Id == except)
				continue;

			_entries[index] = entry with
			{
				EndedAt = entry.EndedAt ?? at,
				EndReason = entry.EndReason ?? (entry.EndedAt is null ? reason : null),
				MustReauthenticate = entry.MustReauthenticate ||
				                     (requireReauthentication && entry.Type is ConnectionType.Osu or ConnectionType.Tourney)
			};
		}

		return Task.CompletedTask;
	}

	public Task WriteActivityAsync(IReadOnlyCollection<SessionActivity> activity,
		CancellationToken cancellationToken = default)
	{
		foreach (var item in activity)
		{
			var index = _entries.FindIndex(entry => entry.Id == item.Id && entry.EndedAt is null);
			if (index < 0) continue;

			_entries[index] = _entries[index] with { LastActiveAt = item.LastActiveAt };
			_tokens[item.Id] = (_tokens[item.Id].Hash, item.ExpiresAt);
		}

		return Task.CompletedTask;
	}

	public Task<bool> TakeReauthenticationAsync(UserRecord user, DeviceId device,
		CancellationToken cancellationToken = default)
	{
		var index = _entries.FindIndex(entry => entry.User.Equals(user) && entry.MustReauthenticate);
		if (index < 0) return Task.FromResult(false);

		_entries[index] = _entries[index] with { MustReauthenticate = false };
		return Task.FromResult(true);
	}

	/// <summary>Reads the stored expiry of a session's token.</summary>
	/// <param name="id">The session.</param>
	/// <returns>When the token expires, or <see langword="null" /> if it has no expiry.</returns>
	public DateTimeOffset? ExpiryOf(Guid id)
	{
		return _tokens.GetValueOrDefault(id).ExpiresAt;
	}

	private bool IsActive(SessionEntry entry, DateTimeOffset now)
	{
		return entry.EndedAt is null && (ExpiryOf(entry.Id) is not { } expiresAt || expiresAt > now);
	}

	private static SessionEntry ToEntry(Session session)
	{
		return new SessionEntry(session.Id, session.User, session.Type, session.Ip, session.ClientInfo,
			session.StartedAt, session.LastActiveAt, session.EndedAt, session.EndReason, session.MustReauthenticate);
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

	public Task AppendAsync(ModerationData entry, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
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
			entries.Count));
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
			matches.Skip(page.Offset).Take(page.Limit).ToList(), matches.Count));
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
	private ServerSettings _settings = new();

	public ServerSettings Current => _settings;

	public ValueTask<ServerSettings> GetAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult(_settings);
	}

	public Task CreateOrUpdateAsync(ServerSettings settings, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_settings = settings;
		return Task.CompletedTask;
	}
}
