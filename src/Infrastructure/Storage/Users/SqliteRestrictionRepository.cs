using System.Collections.Immutable;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Users;
using Basil.Infrastructure.Storage.Batching;
using Basil.Infrastructure.Storage.Caching;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage.Users;

/// <summary>Stores the restrictions of users.</summary>
internal sealed class SqliteRestrictionRepository(DatabaseBatcher batcher, IUserRepository users)
	: CachedRepository<int, Restriction>(batcher), IRestrictionRepository
{
	private readonly IdentityMap<int, ImmutableList<Restriction>> _byUser = new();

	protected override int KeyOf(Restriction item) => item.Id;

	/// <inheritdoc />
	public async Task<Restriction> CreateAsync(RestrictionData data, CancellationToken cancellationToken = default)
	{
		var user = await users.GetAsync(data.User.Id, cancellationToken) ?? data.User;
		var value = new RestrictionData
		{
			User = user,
			Permissions = data.Permissions,
			StartsAt = data.StartsAt,
			EndsAt = data.EndsAt
		};
		var parameters = new
		{
			UserId = value.User.Id,
			Permissions = (long)value.Permissions,
			StartsAt = value.StartsAt.ToUnixTimeMilliseconds(),
			EndsAt = value.EndsAt?.ToUnixTimeMilliseconds()
		};

		var id = await Batcher.WriteAsync((connection, transaction) => connection.QuerySingleAsync<int>(
			"""
			INSERT INTO Restrictions (UserId, Permissions, StartsAt, EndsAt)
			VALUES (@UserId, @Permissions, @StartsAt, @EndsAt)
			RETURNING Id;
			""",
			parameters, transaction), cancellationToken);

		var restriction = Track(new Restriction { Id = id, Value = value });
		_byUser.TryUpdate(user.Id, current => current.Add(restriction));
		return restriction;
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(Restriction restriction, CancellationToken cancellationToken = default)
	{
		var live = Track(restriction);
		_byUser.TryUpdate(live.Value.User.Id, current => Replace(current, live));
		return SaveAsync(live);
	}

	/// <inheritdoc />
	public ValueTask<Restriction?> GetAsync(int id, CancellationToken cancellationToken = default) => FindAsync(id, cancellationToken);

	/// <inheritdoc />
	public async Task<IReadOnlyList<Restriction>> ListAsync(User user, CancellationToken cancellationToken = default)
	{
		var liveUser = await users.GetAsync(user.Id, cancellationToken) ?? user;
		var items = await _byUser.GetOrAddAsync(liveUser.Id, async _ =>
		{
			var rows = await Batcher.ReadAsync(connection => connection.QueryAsync<RestrictionRow>(
				"SELECT Id, UserId, Permissions, StartsAt, EndsAt FROM Restrictions WHERE UserId = @UserId ORDER BY StartsAt, Id",
				new { UserId = liveUser.Id }), cancellationToken);
			var restrictions = ImmutableList.CreateBuilder<Restriction>();
			foreach (var row in rows)
				restrictions.Add(Track(ToRestriction(row, liveUser)));
			return restrictions.ToImmutable();
		});

		return items ?? ImmutableList<Restriction>.Empty;
	}

	protected override async Task<Restriction?> LoadAsync(int key, CancellationToken cancellationToken)
	{
		var row = await Batcher.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<RestrictionRow>(
			"SELECT Id, UserId, Permissions, StartsAt, EndsAt FROM Restrictions WHERE Id = @Id", new { Id = key }), cancellationToken);
		if (row is null)
			return null;

		var user = await users.GetAsync(row.UserId, cancellationToken);
		return user is null ? null : ToRestriction(row, user);
	}

	protected override string WriteSql =>
		"""
		INSERT INTO Restrictions (Id, UserId, Permissions, StartsAt, EndsAt)
		VALUES (@Id, @UserId, @Permissions, @StartsAt, @EndsAt)
		ON CONFLICT(Id) DO UPDATE SET
			UserId = excluded.UserId,
			Permissions = excluded.Permissions,
			StartsAt = excluded.StartsAt,
			EndsAt = excluded.EndsAt;
		""";

	protected override object WriteParameters(Restriction restriction)
	{
		var value = restriction.Value;
		return new
		{
			restriction.Id,
			UserId = value.User.Id,
			Permissions = (long)value.Permissions,
			StartsAt = value.StartsAt.ToUnixTimeMilliseconds(),
			EndsAt = value.EndsAt?.ToUnixTimeMilliseconds()
		};
	}

	private static ImmutableList<Restriction> Replace(ImmutableList<Restriction> current, Restriction item)
	{
		var index = current.FindIndex(restriction => restriction.Id == item.Id);
		return index < 0 ? current.Add(item) : current.SetItem(index, item);
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
