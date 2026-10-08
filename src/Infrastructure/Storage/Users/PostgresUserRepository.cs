using System.Collections.Concurrent;
using System.Globalization;
using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Users;
using Basil.Infrastructure.Storage.Caching;
using Basil.Infrastructure.Storage.Writing;
using Dapper;
using Npgsql;

namespace Basil.Infrastructure.Storage.Users;

/// <summary>Stores registered users.</summary>
internal sealed class PostgresUserRepository(Database database, DatabaseWriter writer)
	: CachedRepository<int, User>(database, writer), IUserRepository
{
	private readonly ConcurrentDictionary<string, int> _idsBySafeName = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<int, string> _safeNamesById = new();

	protected override int KeyOf(User item) => item.Id;

	protected override Root RootOf(User item) => Root.User(item.Id);

	/// <inheritdoc />
	public async Task<User> CreateAsync(UserData data, CancellationToken cancellationToken = default)
	{
		var parameters = new
		{
			data.Name,
			Country = (int)data.Country,
			Permissions = (long)data.Permissions,
			DeletedAt = data.DeletedAt?.ToUniversalTime()
		};
		var id = await Writer.WriteAsync(Root.Server, (connection, transaction) => connection.QuerySingleAsync<int>(
			"""
			insert into users (name, country, permissions, deleted_at)
			values (@Name, @Country, @Permissions, @DeletedAt)
			returning id;
			""",
			parameters, transaction), cancellationToken);

		return TrackUser(new User { Id = id, Value = data });
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(User user, CancellationToken cancellationToken = default)
	{
		var live = TrackUser(user);
		if (!ReferenceEquals(live, user))
		{
			live.Value.Country = user.Value.Country;
			live.Value.Permissions = user.Value.Permissions;
			live.Value.DeletedAt = user.Value.DeletedAt;
		}

		_ = SaveAsync(live);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<bool> RenameAsync(User user, string name, CancellationToken cancellationToken = default)
	{
		_ = new UserData { Name = name };
		var live = TrackUser(user);

		// Whether the name is free is the database's to say, so the user takes the name only once it is committed.
		try
		{
			await Writer.WriteAsync(Root.User(live.Id), (connection, transaction) => connection.ExecuteAsync(
				"update users set name = @Name where id = @Id", new { Name = name, live.Id }, transaction), cancellationToken);
		}
		catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
		{
			return false;
		}

		live.Value.Name = name;
		IndexUser(live);
		return true;
	}

	/// <inheritdoc />
	public ValueTask<User?> GetAsync(int id, CancellationToken cancellationToken = default) =>
		FindUserAsync(id, cancellationToken);

	/// <inheritdoc />
	public async ValueTask<User?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
	{
		var safeName = SafeName(name);
		if (_idsBySafeName.TryGetValue(safeName, out var id))
		{
			var cached = await FindUserAsync(id, cancellationToken);
			if (cached is not null && SafeName(cached.Value.Name) == safeName)
				return cached;

			_idsBySafeName.TryRemove(new KeyValuePair<string, int>(safeName, id));
		}

		var foundId = await Database.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<int?>(
			"select id from users where safe_name = replace(lower(@Name), ' ', '_')",
			new { Name = name }), cancellationToken);
		return foundId is { } found ? await FindUserAsync(found, cancellationToken) : null;
	}

	/// <inheritdoc />
	public async Task<Page<User>> ListAsync(UserQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var (where, parameters) = BuildFilter(query);
		parameters.Add("Limit", page.Limit);
		parameters.Add("Offset", page.Offset);
		var (total, rows) = await Database.ReadAsync(async connection => (
			await connection.ExecuteScalarAsync<int>($"select count(*) from users {where}", parameters),
			await connection.QueryAsync<UserRow>(
				$"select id, name, country, permissions, deleted_at from users {where} order by id limit @Limit offset @Offset",
				parameters)), cancellationToken);

		return new Page<User>([.. rows.Select(row => TrackUser(ToUser(row)))], total);
	}

	protected override async Task<User?> LoadAsync(int key, CancellationToken cancellationToken)
	{
		var row = await Database.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<UserRow>(
			"select id, name, country, permissions, deleted_at from users where id = @Id", new { Id = key }),
			cancellationToken);
		return row is null ? null : ToUser(row);
	}

	protected override string WriteSql =>
		"""
		insert into users (id, name, country, permissions, deleted_at)
		values (@Id, @Name, @Country, @Permissions, @DeletedAt)
		on conflict (id) do update set
			country = excluded.country,
			permissions = excluded.permissions,
			deleted_at = excluded.deleted_at;
		""";

	protected override object WriteParameters(User user)
	{
		var value = user.Value;
		return new
		{
			user.Id,
			value.Name,
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
		var safeName = SafeName(live.Value.Name);
		if (_safeNamesById.TryGetValue(live.Id, out var previous) && previous != safeName)
			_idsBySafeName.TryRemove(new KeyValuePair<string, int>(previous, live.Id));

		_safeNamesById[live.Id] = safeName;
		_idsBySafeName[safeName] = live.Id;
	}

	private static string SafeName(string name)
	{
		return string.Create(name.Length, name, static (characters, value) =>
		{
			for (var index = 0; index < value.Length; index++)
			{
				var character = value[index];
				characters[index] = character is >= 'A' and <= 'Z' ? (char)(character + ('a' - 'A'))
					: character == ' ' ? '_'
					: character;
			}
		});
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
