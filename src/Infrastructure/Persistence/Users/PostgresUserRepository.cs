using System.Collections.Concurrent;
using System.Globalization;
using Basil.Application.Persistence.Common;
using Basil.Application.Persistence.Users;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Memory;
using Basil.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Users;

/// <summary>Stores registered users.</summary>
internal sealed class PostgresUserRepository(DatabaseReader reader, DatabaseWriter writer)
	: MemoryRepository<int, UserRecord>(reader, writer), IUserRepository
{
	private readonly WeakIndex<string, UserRecord> _byName = new();

	// The queued saves of deleted accounts, by the name the deletion frees: a new account of that name waits for them.
	private readonly ConcurrentDictionary<string, Task> _freeing = new();

	/// <inheritdoc />
	public async Task<UserRecord> CreateAsync(UserData data, CancellationToken cancellationToken = default)
	{
		if (_byName.TryGetValue(data.NormalizedName, out var known) &&
		    known.Value.NormalizedName == data.NormalizedName)
			throw AlreadyNamed(data.Name);

		if (_freeing.TryGetValue(data.NormalizedName, out var freeing))
			await Task.WhenAny(freeing);

		var row = ToRow(0, data);
		try
		{
			await Writer.WriteAsync(Root.Server, async (context, token) =>
			{
				context.Users.Add(row);
				await context.SaveChangesAsync(token);
				return row.Id;
			}, cancellationToken);
		}
		catch (Exception exception) when (exception.IsUniqueViolation())
		{
			throw AlreadyNamed(data.Name);
		}

		return TrackUser(new UserRecord { Id = row.Id, Value = data });
	}

	/// <inheritdoc />
	public async Task<UserRecord> ReplaceAsync(int id, UserData data, CancellationToken cancellationToken = default)
	{
		if (_byName.TryGetValue(data.NormalizedName, out var known) &&
		    known.Value.NormalizedName == data.NormalizedName && known.Id != id)
			throw AlreadyNamed(data.Name);

		if (_freeing.TryGetValue(data.NormalizedName, out var freeing))
			await Task.WhenAny(freeing);

		var row = ToRow(id, data);
		try
		{
			await Writer.WriteAsync(Root.User(id),
				(context, token) => context.UpsertAsync(row, stored => stored.Id == id, token), cancellationToken);
		}
		catch (Exception exception) when (exception.IsUniqueViolation())
		{
			throw AlreadyNamed(data.Name);
		}

		// The stored user is a different account now: the instance memory holds for the id is dropped.
		if (Items.TryGetValue(id, out var old)) _byName.Remove(old.Value.NormalizedName, old);
		Items.Remove(id);
		return TrackUser(new UserRecord { Id = id, Value = data });
	}

	/// <inheritdoc />
	public Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default)
	{
		var saved = SaveAsync(user);
		if (user.Value.DeletedAt is not null)
		{
			var name = user.Value.NormalizedName;
			_byName.Remove(name, user);
			_freeing[name] = saved;
			_ = saved.ContinueWith(_ => _freeing.TryRemove(new KeyValuePair<string, Task>(name, saved)),
				TaskScheduler.Default);
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<bool> RenameAsync(UserRecord user, string name, CancellationToken cancellationToken = default)
	{
		_ = new UserData { Name = name };
		var live = TrackUser(user);
		var old = live.Value.NormalizedName;
		var normalized = UserData.NormalizeName(name);

		if (_byName.TryGetValue(normalized, out var known) && known.Value.NormalizedName == normalized &&
		    known != live)
			return false;

		// Whether the name is free is the database's to say, so the user takes the name only once it is committed.
		var id = live.Id;
		try
		{
			await Writer.WriteAsync(Root.User(id), async (context, token) => await context.Users
				.Where(row => row.Id == id)
				.ExecuteUpdateAsync(set => set
					.SetProperty(row => row.Name, name)
					.SetProperty(row => row.NormalizedName, normalized), token), cancellationToken);
		}
		catch (Exception exception) when (exception.IsUniqueViolation())
		{
			return false;
		}

		live.Value.Name = name;
		_byName.Remove(old, live);
		IndexUser(live);
		return true;
	}

	/// <inheritdoc />
	public ValueTask<UserRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		return FindUserAsync(id, cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask<UserRecord?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
	{
		var normalized = UserData.NormalizeName(name);
		if (_byName.TryGetValue(normalized, out var known) && known.Value.NormalizedName == normalized)
			return known;

		var foundId = await Reader.ReadAsync(context => context.Users
			.Where(row => row.NormalizedName == normalized && row.DeletedAt == null)
			.Select(row => (int?)row.Id)
			.FirstOrDefaultAsync(cancellationToken), cancellationToken);
		return foundId is { } found ? await FindUserAsync(found, cancellationToken) : null;
	}

	/// <inheritdoc />
	public async Task<Page<UserRecord>> ListAsync(UserQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var (total, rows) = await Reader.ReadAsync(async context =>
		{
			var matching = Filter(context.Users, query);
			return (await matching.CountAsync(cancellationToken),
				await matching.OrderBy(row => row.Id).Skip(page.Offset).Take(page.Limit)
					.ToListAsync(cancellationToken));
		}, cancellationToken);

		return new Page<UserRecord>([.. rows.Select(row => TrackUser(ToUser(row)))], total);
	}

	protected override int KeyOf(UserRecord item)
	{
		return item.Id;
	}

	protected override Root RootOf(UserRecord item)
	{
		return Root.User(item.Id);
	}

	protected override void CopyTo(UserRecord live, UserRecord from)
	{
		live.Value.Country = from.Value.Country;
		live.Value.Permissions = from.Value.Permissions;
		// A deletion is permanent: an older copy that does not know it must not take it back.
		if (from.Value.DeletedAt is not null) live.Value.DeletedAt = from.Value.DeletedAt;
		if (live.Value.DeletedAt is not null)
			_byName.Remove(live.Value.NormalizedName, live);
	}

	protected override async Task<UserRecord?> LoadAsync(int key)
	{
		var row = await Reader.ReadAsync(context => context.Users.Where(candidate => candidate.Id == key)
			.FirstOrDefaultAsync());
		return row is null ? null : ToUser(row);
	}

	protected override WriteCommand SaveCommand(UserRecord user)
	{
		var id = user.Id;
		var country = (int)user.Value.Country;
		var permissions = (long)user.Value.Permissions;
		var deletedAt = user.Value.DeletedAt?.ToUniversalTime();
		return new WriteCommand((context, token) => context.Users.Where(row => row.Id == id)
			.ExecuteUpdateAsync(set => set
				.SetProperty(row => row.Country, country)
				.SetProperty(row => row.Permissions, permissions)
				.SetProperty(row => row.DeletedAt, deletedAt), token));
	}

	private UserRecord TrackUser(UserRecord user)
	{
		var live = Track(user);
		IndexUser(live);
		return live;
	}

	private async ValueTask<UserRecord?> FindUserAsync(int id, CancellationToken cancellationToken)
	{
		var user = await FindAsync(id, cancellationToken);
		if (user is not null)
			IndexUser(user);
		return user;
	}

	/// <summary>Indexes a live user by name; a user who is deleted is not indexed, so their name stays free.</summary>
	private void IndexUser(UserRecord live)
	{
		if (live.Value.DeletedAt is null)
			_byName.Set(live.Value.NormalizedName, live);
	}

	/// <summary>Narrows the users to the ones a query asks for.</summary>
	private static IQueryable<UserRow> Filter(IQueryable<UserRow> users, UserQuery query)
	{
		if (query.Deleted is { } deleted)
			users = deleted ? users.Where(row => row.DeletedAt != null) : users.Where(row => row.DeletedAt == null);

		if (query.Countries is { Count: > 0 } countries)
		{
			var codes = countries.Select(country => (int)country).ToList();
			users = users.Where(row => codes.Contains(row.Country));
		}

		if (query.Permissions is { } permissions)
		{
			var mask = (long)permissions;
			users = users.Where(row => (row.Permissions & mask) == mask);
		}

		if (query.Text is { } text)
		{
			var pattern = LikePattern.Containing(text.ToLowerInvariant().Replace(' ', '_'));
			users = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
				? users.Where(row => row.Id == id || EF.Functions.Like(row.NormalizedName, pattern, LikePattern.Escape))
				: users.Where(row => EF.Functions.Like(row.NormalizedName, pattern, LikePattern.Escape));
		}

		return users;
	}

	/// <summary>Builds the row of an account; an id of zero stands for an account that has none yet.</summary>
	/// <param name="id">The id of the account the row stores.</param>
	/// <param name="data">The data of the account.</param>
	/// <returns>The row to store.</returns>
	private static UserRow ToRow(int id, UserData data)
	{
		return new UserRow
		{
			Id = id,
			Name = data.Name,
			NormalizedName = data.NormalizedName,
			Country = (int)data.Country,
			Permissions = (long)data.Permissions,
			DeletedAt = data.DeletedAt?.ToUniversalTime()
		};
	}

	/// <summary>Builds the refusal for a name that another account already holds.</summary>
	/// <param name="name">The name that is taken.</param>
	/// <returns>The refusal to throw.</returns>
	private static AlreadyExistsException AlreadyNamed(string name)
	{
		return new AlreadyExistsException($"A user is already named {name}.");
	}

	/// <summary>Builds a user from a stored row.</summary>
	private static UserRecord ToUser(UserRow row)
	{
		return new UserRecord
		{
			Id = row.Id,
			Value = new UserData
			{
				Name = row.Name,
				Country = (Country)row.Country,
				Permissions = (UserPermissions)(ulong)row.Permissions,
				DeletedAt = row.DeletedAt
			}
		};
	}
}