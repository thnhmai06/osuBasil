using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Users;
using Dapper;

namespace Basil.Infrastructure.Storage.Users;

/// <summary>Stores the restrictions of users.</summary>
internal sealed class SqliteRestrictionRepository(Database database, IUserRepository users) : IRestrictionRepository
{
	/// <inheritdoc />
	public async Task<Restriction> CreateAsync(RestrictionData data, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var id = await connection.QuerySingleAsync<int>(
			"""
			INSERT INTO Restrictions (UserId, Permissions, StartsAt, EndsAt)
			VALUES (@UserId, @Permissions, @StartsAt, @EndsAt)
			RETURNING Id;
			""",
			new
			{
				UserId = data.User.Id,
				Permissions = (long)data.Permissions,
				StartsAt = data.StartsAt.ToUnixTimeMilliseconds(),
				EndsAt = data.EndsAt?.ToUnixTimeMilliseconds()
			});

		return new Restriction { Id = id, Value = data };
	}

	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(Restriction restriction, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		await connection.ExecuteAsync(
			"""
			INSERT INTO Restrictions (Id, UserId, Permissions, StartsAt, EndsAt)
			VALUES (@Id, @UserId, @Permissions, @StartsAt, @EndsAt)
			ON CONFLICT(Id) DO UPDATE SET
				UserId = excluded.UserId,
				Permissions = excluded.Permissions,
				StartsAt = excluded.StartsAt,
				EndsAt = excluded.EndsAt;
			""",
			new
			{
				restriction.Id,
				UserId = restriction.Value.User.Id,
				Permissions = (long)restriction.Value.Permissions,
				StartsAt = restriction.Value.StartsAt.ToUnixTimeMilliseconds(),
				EndsAt = restriction.Value.EndsAt?.ToUnixTimeMilliseconds()
			});
	}

	/// <inheritdoc />
	public async ValueTask<Restriction?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var row = await connection.QuerySingleOrDefaultAsync<RestrictionRow>(
			"SELECT Id, UserId, Permissions, StartsAt, EndsAt FROM Restrictions WHERE Id = @Id",
			new { Id = id });
		if (row is null)
			return null;

		var user = await users.GetAsync(row.UserId, cancellationToken);
		return user is null ? null : ToRestriction(row, user);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Restriction>> ListAsync(User user, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var rows = await connection.QueryAsync<RestrictionRow>(
			"SELECT Id, UserId, Permissions, StartsAt, EndsAt FROM Restrictions WHERE UserId = @UserId ORDER BY StartsAt, Id",
			new { UserId = user.Id });

		return [.. rows.Select(row => ToRestriction(row, user))];
	}

	/// <summary>Builds a restriction from a stored row.</summary>
	private static Restriction ToRestriction(RestrictionRow row, User user)
	{
		return new Restriction
		{
			Id = row.Id,
			Value = new RestrictionData
			{
				User = user,
				Permissions = (Permissions)(ulong)row.Permissions,
				StartsAt = DateTimeOffset.FromUnixTimeMilliseconds(row.StartsAt),
				EndsAt = row.EndsAt is { } endsAt ? DateTimeOffset.FromUnixTimeMilliseconds(endsAt) : null
			}
		};
	}

	/// <summary>A stored row of the Restrictions table.</summary>
	private sealed class RestrictionRow
	{
		public int Id { get; set; }
		public int UserId { get; set; }
		public long Permissions { get; set; }
		public long StartsAt { get; set; }
		public long? EndsAt { get; set; }
	}
}
