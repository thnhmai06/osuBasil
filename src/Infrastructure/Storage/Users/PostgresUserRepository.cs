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
	: MemoryRepository<int, UserRecord>(reader, writer), IUserRepository
{
	/// <summary>The expression that gives a stored name its identifying form: lower case, with spaces as underscores.</summary>
	private const string NormalizedNameSql = "lower(replace(name, ' ', '_'))";

	private readonly WeakIndex<string, UserRecord> _byName = new();

	protected override string WriteSql =>
		"""
		update users set
			country = @Country,
			permissions = @Permissions,
			deleted_at = @DeletedAt
		where id = @Id;
		""";

	/// <inheritdoc />
	public async Task<UserRecord> CreateAsync(UserData data, CancellationToken cancellationToken = default)
	{
		if (_byName.TryGetValue(data.NormalizedName, out var known) && known.Value.NormalizedName == data.NormalizedName)
			throw new AlreadyExistsException($"A user is already named {data.Name}.");

		var parameters = new
		{
			data.Name,
			Country = (int)data.Country,
			Permissions = (long)data.Permissions,
			DeletedAt = data.DeletedAt?.ToUniversalTime()
		};
		int id;
		try
		{
			id = await Writer.WriteAsync(Root.Server, (connection, transaction) => connection.QuerySingleAsync<int>(
				"""
				insert into users (name, country, permissions, deleted_at)
				values (@Name, @Country, @Permissions, @DeletedAt)
				returning id;
				""",
				parameters, transaction), cancellationToken);
		}
		catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
		{
			throw new AlreadyExistsException($"A user is already named {data.Name}.");
		}

		return TrackUser(new UserRecord { Id = id, Value = data });
	}

	/// <inheritdoc />
	public Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default)
	{
		_ = SaveAsync(user);
		if (user.Value.DeletedAt is not null)
			_byName.Remove(user.Value.NormalizedName, user);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<bool> RenameAsync(UserRecord user, string name, CancellationToken cancellationToken = default)
	{
		_ = new UserData { Name = name };
		var live = TrackUser(user);
		var old = live.Value.NormalizedName;
		var newSafe = UserData.NormalizeName(name);

		if (_byName.TryGetValue(newSafe, out var known) && known.Value.NormalizedName == newSafe && known != live)
			return false;

		// Whether the name is free is the database's to say, so the user takes the name only once it is committed.
		try
		{
			await Writer.WriteAsync(Root.User(live.Id), (connection, transaction) => connection.ExecuteAsync(
				"update users set name = @Name where id = @Id",
				new { Name = name, live.Id }, transaction), cancellationToken);
		}
		catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
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
		var safeName = UserData.NormalizeName(name);
		if (_byName.TryGetValue(safeName, out var known) && known.Value.NormalizedName == safeName)
			return known;

		var foundId = await Reader.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<int?>(
			$"select id from users where {NormalizedNameSql} = @NormalizedName and deleted_at is null",
			new { NormalizedName = safeName }), cancellationToken);
		return foundId is { } found ? await FindUserAsync(found, cancellationToken) : null;
	}

	/// <inheritdoc />
	public async Task<Page<UserRecord>> ListAsync(UserQuery query, PageRequest page,
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
		live.Value.DeletedAt = from.Value.DeletedAt;
		if (live.Value.DeletedAt is not null)
			_byName.Remove(live.Value.NormalizedName, live);
	}

	protected override async Task<UserRecord?> LoadAsync(int key)
	{
		var row = await Reader.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<UserRow>(
			"select id, name, country, permissions, deleted_at from users where id = @Id", new { Id = key }));
		return row is null ? null : ToUser(row);
	}

	protected override object WriteParameters(UserRecord user)
	{
		var value = user.Value;
		return new
		{
			user.Id,
			value.Name,
			NormalizedName = value.NormalizedName,
			Country = (int)value.Country,
			Permissions = (long)value.Permissions,
			DeletedAt = value.DeletedAt?.ToUniversalTime()
		};
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

	/// <summary>Builds the shared filter and parameters for a user listing.</summary>
	private static (string Where, DynamicParameters Parameters) BuildFilter(UserQuery query)
	{
		var conditions = new List<string>();
		var parameters = new DynamicParameters();
		if (query.Deleted is { } deleted) conditions.Add(deleted ? "deleted_at is not null" : "deleted_at is null");
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
				conditions.Add($"(id = @TextId OR {NormalizedNameSql} LIKE @Pattern)");
				parameters.Add("TextId", id);
			}
			else
			{
				conditions.Add($"{NormalizedNameSql} LIKE @Pattern");
			}
		}

		return (conditions.Count == 0 ? "" : $"where {string.Join(" AND ", conditions)}", parameters);
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