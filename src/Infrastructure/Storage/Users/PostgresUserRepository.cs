using System.Globalization;
using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Users;
using Basil.Infrastructure.Storage.Common.Database;
using Basil.Infrastructure.Storage.Common.Queries;
using Basil.Infrastructure.Storage.Common.Writing;
using Basil.Infrastructure.Storage.Memory;
using Dapper;
using Npgsql;

namespace Basil.Infrastructure.Storage.Users;

/// <summary>Stores registered users.</summary>
internal sealed class PostgresUserRepository(DatabaseReader reader, DatabaseWriter writer)
	: MemoryRepository<int, User>(reader, writer), IUserRepository
{
	private readonly WeakIndex<string, User> _byName = new();

	protected override string WriteSql =>
		"""
		insert into users (id, name, safe_name, country, permissions, deleted_at)
		values (@Id, @Name, @SafeName, @Country, @Permissions, @DeletedAt)
		on conflict (id) do update set
			country = excluded.country,
			permissions = excluded.permissions,
			deleted_at = excluded.deleted_at;
		""";

	/// <inheritdoc />
	public async Task<User> CreateAsync(UserData data, CancellationToken cancellationToken = default)
	{
		if (_byName.TryGet(data.SafeName, out var known) && known.Value.SafeName == data.SafeName)
			throw new AlreadyExistsException($"A user is already named {data.Name}.");

		var parameters = new
		{
			data.Name,
			data.SafeName,
			Country = (int)data.Country,
			Permissions = (long)data.Permissions,
			DeletedAt = data.DeletedAt?.ToUniversalTime()
		};
		int id;
		try
		{
			id = await Writer.WriteAsync(Root.Server, (connection, transaction) => connection.QuerySingleAsync<int>(
				"""
				insert into users (name, safe_name, country, permissions, deleted_at)
				values (@Name, @SafeName, @Country, @Permissions, @DeletedAt)
				returning id;
				""",
				parameters, transaction), cancellationToken);
		}
		catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
		{
			throw new AlreadyExistsException($"A user is already named {data.Name}.");
		}

		return TrackUser(new User { Id = id, Value = data });
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(User user, CancellationToken cancellationToken = default)
	{
		_ = SaveAsync(user);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<bool> RenameAsync(User user, string name, CancellationToken cancellationToken = default)
	{
		_ = new UserData { Name = name };
		var live = TrackUser(user);
		var old = live.Value.SafeName;
		var newSafe = UserData.SafeNameOf(name);

		if (_byName.TryGet(newSafe, out var known) && known.Value.SafeName == newSafe && known != live)
			return false;

		// Whether the name is free is the database's to say, so the user takes the name only once it is committed.
		try
		{
			await Writer.WriteAsync(Root.User(live.Id), (connection, transaction) => connection.ExecuteAsync(
				"update users set name = @Name, safe_name = @SafeName where id = @Id",
				new { Name = name, SafeName = newSafe, live.Id }, transaction), cancellationToken);
		}
		catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
		{
			return false;
		}

		live.Value.Name = name;
		_byName.Remove(old, live);
		_byName.Set(live.Value.SafeName, live);
		return true;
	}

	/// <inheritdoc />
	public ValueTask<User?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		return FindUserAsync(id, cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask<User?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
	{
		var safeName = UserData.SafeNameOf(name);
		if (_byName.TryGet(safeName, out var known) && known.Value.SafeName == safeName)
			return known;

		var foundId = await Reader.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<int?>(
			"select id from users where safe_name = @SafeName", new { SafeName = safeName }), cancellationToken);
		return foundId is { } found ? await FindUserAsync(found, cancellationToken) : null;
	}

	/// <inheritdoc />
	public async Task<Page<User>> ListAsync(UserQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var (where, parameters) = BuildFilter(query);
		parameters.Add("Limit", page.Limit);
		parameters.Add("Offset", page.Offset);
		var (total, rows) = await Reader.ReadAsync(async connection => (
			await connection.ExecuteScalarAsync<int>($"select count(*) from users {where}", parameters),
			await connection.QueryAsync<UserRow>(
				$"select id, name, country, permissions, deleted_at from users {where} order by id limit @Limit offset @Offset",
				parameters)), cancellationToken);

		return new Page<User>([.. rows.Select(row => TrackUser(ToUser(row)))], total);
	}

	protected override int KeyOf(User item)
	{
		return item.Id;
	}

	protected override Root RootOf(User item)
	{
		return Root.User(item.Id);
	}

	protected override void CopyTo(User live, User from)
	{
		live.Value.Country = from.Value.Country;
		live.Value.Permissions = from.Value.Permissions;
		live.Value.DeletedAt = from.Value.DeletedAt;
	}

	protected override async Task<User?> LoadAsync(int key)
	{
		var row = await Reader.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<UserRow>(
			"select id, name, country, permissions, deleted_at from users where id = @Id", new { Id = key }));
		return row is null ? null : ToUser(row);
	}

	protected override object WriteParameters(User user)
	{
		var value = user.Value;
		return new
		{
			user.Id,
			value.Name,
			value.SafeName,
			Country = (int)value.Country,
			Permissions = (long)value.Permissions,
			DeletedAt = value.DeletedAt?.ToUniversalTime()
		};
	}

	private User TrackUser(User user)
	{
		var live = Track(user);
		IndexUser(live);
		return live;
	}

	private async ValueTask<User?> FindUserAsync(int id, CancellationToken cancellationToken)
	{
		var user = await FindAsync(id, cancellationToken);
		if (user is not null)
			IndexUser(user);
		return user;
	}

	private void IndexUser(User live)
	{
		_byName.Set(live.Value.SafeName, live);
	}

	/// <summary>Builds the shared filter and parameters for a user listing.</summary>
	private static (string Where, DynamicParameters Parameters) BuildFilter(UserQuery query)
	{
		var conditions = new List<string>();
		var parameters = new DynamicParameters();
		if (!query.IncludeDeleted)
			conditions.Add("deleted_at is null");
		if (query.Countries is { Count: > 0 } countries)
		{
			conditions.Add("country = any(@Countries)");
			parameters.Add("Countries", countries.Select(country => (int)country).ToArray());
		}

		if (query.Permissions is { } permissions)
		{
			conditions.Add("(permissions & @Permissions) = @Permissions");
			parameters.Add("Permissions", (long)permissions);
		}

		if (query.Text is { } text)
		{
			var pattern = LikePattern.Containing(text.ToLowerInvariant().Replace(' ', '_'));
			parameters.Add("Pattern", pattern);
			if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
			{
				conditions.Add("(id = @TextId OR safe_name LIKE @Pattern)");
				parameters.Add("TextId", id);
			}
			else
			{
				conditions.Add("safe_name LIKE @Pattern");
			}
		}

		return (conditions.Count == 0 ? "" : $"where {string.Join(" AND ", conditions)}", parameters);
	}

	/// <summary>Builds a user from a stored row.</summary>
	private static User ToUser(UserRow row)
	{
		return new User
		{
			Id = row.Id,
			Value = new UserData
			{
				Name = row.Name,
				Country = (Country)row.Country,
				Permissions = (Permissions)(ulong)row.Permissions,
				DeletedAt = row.DeletedAt
			}
		};
	}

	/// <summary>A stored row of the <c>users</c> table.</summary>
	private sealed class UserRow
	{
		public int Id { get; set; }
		public string Name { get; set; } = "";
		public int Country { get; set; }
		public long Permissions { get; set; }
		public DateTimeOffset? DeletedAt { get; set; }
	}
}