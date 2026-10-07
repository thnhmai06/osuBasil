using System.Collections.Concurrent;
using System.Globalization;
using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Users;
using Basil.Infrastructure.Storage.Caching;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage.Users;

/// <summary>Stores registered users.</summary>
internal sealed class SqliteUserRepository(Database database, WriteBuffer buffer)
	: CachedRepository<int, User>(database, buffer), IUserRepository
{
	private readonly ConcurrentDictionary<string, int> _idsBySafeName = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<int, string> _safeNamesById = new();

	protected override int KeyOf(User item) => item.Id;

	/// <inheritdoc />
	public async Task<User> CreateAsync(UserData data, CancellationToken cancellationToken = default)
	{
		await using var connection = await OpenAsync(cancellationToken);
		var id = await connection.QuerySingleAsync<int>(
			"""
			INSERT INTO Users (Name, Country, Permissions, DeletedAt)
			VALUES (@Name, @Country, @Permissions, @DeletedAt)
			RETURNING Id;
			""",
			new
			{
				data.Name,
				Country = (long)data.Country,
				Permissions = (long)data.Permissions,
				DeletedAt = data.DeletedAt?.ToUnixTimeMilliseconds()
			});

		return TrackUser(new User { Id = id, Value = data });
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(User user, CancellationToken cancellationToken = default)
	{
		var live = TrackUser(user);
		if (!ReferenceEquals(live, user))
		{
			live.Value.Name = user.Value.Name;
			live.Value.Country = user.Value.Country;
			live.Value.Permissions = user.Value.Permissions;
			live.Value.DeletedAt = user.Value.DeletedAt;
			IndexUser(live);
		}

		Save(live);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public ValueTask<User?> GetAsync(int id, CancellationToken cancellationToken = default) => FindUserAsync(id, cancellationToken);

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

		await using var connection = await OpenAsync(cancellationToken);
		var foundId = await connection.QuerySingleOrDefaultAsync<int?>(
			"SELECT Id FROM Users WHERE SafeName = replace(lower(@Name), ' ', '_')",
			new { Name = name });
		return foundId is { } found ? await FindUserAsync(found, cancellationToken) : null;
	}

	/// <inheritdoc />
	public async Task<Page<User>> ListAsync(UserQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var (where, parameters) = BuildFilter(query);
		await using var connection = await OpenAsync(cancellationToken);
		var total = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM Users {where}", parameters);
		parameters.Add("Limit", page.Limit);
		parameters.Add("Offset", page.Offset);
		var rows = await connection.QueryAsync<UserRow>(
			$"SELECT Id, Name, Country, Permissions, DeletedAt FROM Users {where} ORDER BY Id LIMIT @Limit OFFSET @Offset",
			parameters);

		return new Page<User>([.. rows.Select(row => TrackUser(ToUser(row)))], total);
	}

	protected override async Task<User?> ReadAsync(SqliteConnection connection, int key,
		CancellationToken cancellationToken)
	{
		var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
			"SELECT Id, Name, Country, Permissions, DeletedAt FROM Users WHERE Id = @Id", new { Id = key });
		return row is null ? null : ToUser(row);
	}

	protected override async Task WriteAsync(SqliteConnection connection, SqliteTransaction transaction, User user)
	{
		var value = user.Value;
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
				value.Name,
				Country = (long)value.Country,
				Permissions = (long)value.Permissions,
				DeletedAt = value.DeletedAt?.ToUnixTimeMilliseconds()
			}, transaction);
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

		return (conditions.Count == 0 ? "" : $"WHERE {string.Join(" AND ", conditions)}", parameters);
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
				DeletedAt = row.DeletedAt is { } deletedAt ? DateTimeOffset.FromUnixTimeMilliseconds(deletedAt) : null
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
