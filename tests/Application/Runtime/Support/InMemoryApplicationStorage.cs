using Basil.Application.Persistence.Auth;
using Basil.Application.Persistence.Common;
using Basil.Application.Persistence.Content;
using Basil.Application.Persistence.Users;
using Basil.Domain;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Tests.Support;

internal sealed class InMemoryUserRepository : IUserRepository
{
	private readonly Dictionary<int, UserRecord> _users = [];
	private int _nextId = 2;

	public UserRecord Seed(UserData data, int? id = null)
	{
		var userId = id ?? _nextId++;
		_nextId = Math.Max(_nextId, userId + 1);
		var user = new UserRecord { Id = userId, Value = data };
		_users[user.Id] = user;
		return user;
	}

	public Task<UserRecord> CreateAsync(UserData data, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_users.Values.Any(user => user.Value.DeletedAt is null &&
									  user.Value.NormalizedName == data.NormalizedName))
			throw new AlreadyExistsException(data.Name);

		return Task.FromResult(Seed(data));
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
}

internal sealed class InMemoryRestrictionRepository : IRestrictionRepository
{
	private readonly Dictionary<int, List<Restriction>> _restrictions = [];
	private int _nextId = 1;

	public Restriction Seed(UserRecord user, Permissions permissions, DateTimeOffset startsAt,
		DateTimeOffset? endsAt = null)
	{
		var restriction = new Restriction
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

	public Task<Restriction> CreateAsync(RestrictionData data, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var restriction = new Restriction { Id = _nextId++, Value = data };
		GetFor(data.User).Add(restriction);
		return Task.FromResult(restriction);
	}

	public Task UpdateAsync(Restriction restriction, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.CompletedTask;
	}

	public ValueTask<Restriction?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return ValueTask.FromResult(_restrictions.Values.SelectMany(items => items)
			.FirstOrDefault(restriction => restriction.Id == id));
	}

	public Task<IReadOnlyList<Restriction>> ListAsync(UserRecord user,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult<IReadOnlyList<Restriction>>(GetFor(user));
	}

	private List<Restriction> GetFor(UserRecord user)
	{
		return _restrictions.GetValueOrDefault(user.Id) ?? AddList(user.Id);
	}

	private List<Restriction> AddList(int userId)
	{
		var list = new List<Restriction>();
		_restrictions.Add(userId, list);
		return list;
	}
}

internal sealed class InMemoryLoginRepository : ILoginRepository
{
	private readonly List<LoginEntry> _entries = [];

	public IReadOnlyList<LoginEntry> Entries => _entries;

	public Task RecordAsync(Login login, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_entries.Add(new LoginEntry(login.User, login.Ip, login.Timestamp));
		return Task.CompletedTask;
	}

	public ValueTask<Page<LoginEntry>> ListAsync(UserRecord user, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var entries = _entries.Where(entry => entry.User.Equals(user)).Reverse().ToList();
		return ValueTask.FromResult(new Page<LoginEntry>(entries.Skip(page.Offset).Take(page.Limit).ToList(),
			entries.Count));
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
	private readonly List<ModerationRecord> _entries = [];
	private readonly List<ModerationData> _values = [];
	private long _nextId = 1;

	public IReadOnlyList<ModerationData> Entries => _values;

	public Task AppendAsync(ModerationData entry, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		_entries.Add(new ModerationRecord { Id = _nextId++, Value = entry });
		_values.Add(entry);
		return Task.CompletedTask;
	}

	public ValueTask<Page<ModerationRecord>> ListAsync(UserRecord target, Interval<DateTimeOffset>? within,
		PageRequest page, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var entries = _entries.Where(entry => entry.Value.Target.Equals(target) &&
											  (within is null || within.Value.Contains(entry.Value.Timestamp)))
			.Reverse().ToList();
		return ValueTask.FromResult(new Page<ModerationRecord>(entries.Skip(page.Offset).Take(page.Limit).ToList(),
			entries.Count));
	}
}

internal sealed class InMemorySettingsRepository : ISettingsRepository
{
	private ServerSettings _settings = new();

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