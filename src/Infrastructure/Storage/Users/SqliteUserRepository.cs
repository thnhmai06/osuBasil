using System.Globalization;
using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Users;
using Dapper;

namespace Basil.Infrastructure.Storage.Users;

/// <summary>Stores registered users.</summary>
internal sealed class SqliteUserRepository(Database database) : IUserRepository
{
	/// <inheritdoc />
	public async Task<User> CreateAsync(UserData data, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var id = await connection.QuerySingleAsync<int>(
			"""
			INSERT INTO Users (Name, Country, Permissions, DeletedAt)
			VALUES (@Name, @Country, @Permissions, @DeletedAt)
			RETURNING Id;
			""",
			new
			{
				Name = data.Name,
				Country = (long)data.Country,
				Permissions = (long)data.Permissions,
				DeletedAt = data.DeletedAt?.ToUnixTimeMilliseconds()
			});

		return new User { Id = id, Value = data };
	}

	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(User user, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		await connection.ExecuteAsync(
			"""
			INSERT INTO Users (Id, Name, Country, Permissions, DeletedAt)
			VALUES (@Id, @Name, @Country, @Permissions, @DeletedAt)
			ON CONFLICT(Id) DO UPDATE SET
				Name = excluded.Name,
				Country = excluded.Country,
				Permissions = excluded.Permissions,
				DeletedAt = excluded.DeletedAt;
			""",
			new
			{
				user.Id,
				Name = user.Value.Name,
				Country = (long)user.Value.Country,
				Permissions = (long)user.Value.Permissions,
				DeletedAt = user.Value.DeletedAt?.ToUnixTimeMilliseconds()
			});
	}

	/// <inheritdoc />
	public async ValueTask<User?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
			"SELECT Id, Name, Country, Permissions, DeletedAt FROM Users WHERE Id = @Id",
			new { Id = id });
		return row is null ? null : ToUser(row);
	}

	/// <inheritdoc />
	public async ValueTask<User?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
			"SELECT Id, Name, Country, Permissions, DeletedAt FROM Users WHERE SafeName = replace(lower(@Name), ' ', '_')",
			new { Name = name });
		return row is null ? null : ToUser(row);
	}

	/// <inheritdoc />
	public async Task<Page<User>> ListAsync(UserQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var (where, parameters) = BuildFilter(query);

		await using var connection = await database.OpenAsync(cancellationToken);
		var total = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM Users {where}", parameters);

		parameters.Add("Limit", page.Limit);
		parameters.Add("Offset", page.Offset);
		var rows = await connection.QueryAsync<UserRow>(
			$"""
			 SELECT Id, Name, Country, Permissions, DeletedAt FROM Users
			 {where}
			 ORDER BY Id LIMIT @Limit OFFSET @Offset
			 """,
			parameters);

		return new Page<User>([.. rows.Select(ToUser)], total);
	}

	/// <summary>Builds the shared filter and parameters for a user listing.</summary>
	private static (string Where, DynamicParameters Parameters) BuildFilter(UserQuery query)
	{
		var conditions = new List<string>();
		var parameters = new DynamicParameters();

		if (!query.IncludeDeleted)
			conditions.Add("DeletedAt IS NULL");

		if (query.Countries is { Count: > 0 } countries)
		{
			conditions.Add("Country IN @Countries");
			parameters.Add("Countries", countries.Select(country => (long)country).ToList());
		}

		if (query.Permissions is { } permissions)
		{
			conditions.Add("(Permissions & @Permissions) = @Permissions");
			parameters.Add("Permissions", (long)permissions);
		}

		if (query.Text is { } text)
		{
			if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
			{
				conditions.Add("(Id = @TextId OR SafeName LIKE '%' || replace(lower(@Text), ' ', '_') || '%')");
				parameters.Add("TextId", id);
			}
			else
			{
				conditions.Add("SafeName LIKE '%' || replace(lower(@Text), ' ', '_') || '%'");
			}

			parameters.Add("Text", text);
		}

		var where = conditions.Count == 0 ? "" : $"WHERE {string.Join(" AND ", conditions)}";
		return (where, parameters);
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
				DeletedAt = row.DeletedAt is { } deletedAt
					? DateTimeOffset.FromUnixTimeMilliseconds(deletedAt)
					: null
			}
		};
	}

	/// <summary>A stored row of the Users table.</summary>
	private sealed class UserRow
	{
		public int Id { get; set; }
		public string Name { get; set; } = "";
		public long Country { get; set; }
		public long Permissions { get; set; }
		public long? DeletedAt { get; set; }
	}
}
