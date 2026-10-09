using System.Collections.Immutable;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Users;
using Basil.Infrastructure.Storage.Memory;
using Basil.Infrastructure.Storage.Writing;
using Dapper;

namespace Basil.Infrastructure.Storage.Users;

/// <summary>Stores the restrictions of users.</summary>
internal sealed class PostgresRestrictionRepository(
	Database database,
	DatabaseWriter writer,
	IUserRepository users)
	: MemoryRepository<int, Restriction>(database, writer), IRestrictionRepository
{
	private readonly OwnedLists<int, User, Restriction> _byUser = new();

	protected override string WriteSql =>
		"""
		insert into restrictions (id, user_id, permissions, starts_at, ends_at)
		values (@Id, @UserId, @Permissions, @StartsAt, @EndsAt)
		on conflict (id) do update set
			user_id = excluded.user_id,
			permissions = excluded.permissions,
			starts_at = excluded.starts_at,
			ends_at = excluded.ends_at;
		""";

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
			StartsAt = value.StartsAt.ToUniversalTime(),
			EndsAt = value.EndsAt?.ToUniversalTime()
		};

		var id = await Writer.WriteAsync(Root.User(user.Id), (connection, transaction) =>
			connection.QuerySingleAsync<int>(
				"""
				insert into restrictions (user_id, permissions, starts_at, ends_at)
				values (@UserId, @Permissions, @StartsAt, @EndsAt)
				returning id;
				""",
				parameters, transaction), cancellationToken);

		var restriction = Track(new Restriction { Id = id, Value = value });
		_byUser.Change(user.Id, user, current => Replace(current, restriction), Task.CompletedTask);
		return restriction;
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(Restriction restriction, CancellationToken cancellationToken = default)
	{
		var saved = SaveAsync(restriction);
		var live = Track(restriction);
		_byUser.Change(live.Value.User.Id, live.Value.User, current => Replace(current, live), saved);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public ValueTask<Restriction?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		return FindAsync(id, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Restriction>> ListAsync(User user, CancellationToken cancellationToken = default)
	{
		var liveUser = await users.GetAsync(user.Id, cancellationToken) ?? user;
		return await _byUser.GetOrLoadAsync(liveUser.Id, liveUser, async () =>
		{
			var rows = await Database.ReadAsync(connection => connection.QueryAsync<RestrictionRow>(
				"select id, user_id, permissions, starts_at, ends_at from restrictions where user_id = @UserId order by starts_at, id",
				new { UserId = liveUser.Id }), cancellationToken);
			var restrictions = ImmutableList.CreateBuilder<Restriction>();
			foreach (var row in rows)
				restrictions.Add(Track(ToRestriction(row, liveUser)));
			return restrictions.ToImmutable();
		});
	}

	protected override int KeyOf(Restriction item)
	{
		return item.Id;
	}

	protected override Root RootOf(Restriction item)
	{
		return Root.User(item.Value.User.Id);
	}

	protected override void CopyTo(Restriction live, Restriction from)
	{
		live.Value.Permissions = from.Value.Permissions;
		live.Value.EndsAt = from.Value.EndsAt;
	}

	protected override async Task<Restriction?> LoadAsync(int key)
	{
		var row = await Database.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<RestrictionRow>(
			"select id, user_id, permissions, starts_at, ends_at from restrictions where id = @Id", new { Id = key }));
		if (row is null)
			return null;

		var user = await users.GetAsync(row.UserId);
		return user is null ? null : ToRestriction(row, user);
	}

	protected override object WriteParameters(Restriction restriction)
	{
		var value = restriction.Value;
		return new
		{
			restriction.Id,
			UserId = value.User.Id,
			Permissions = (long)value.Permissions,
			StartsAt = value.StartsAt.ToUniversalTime(),
			EndsAt = value.EndsAt?.ToUniversalTime()
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
				StartsAt = row.StartsAt,
				EndsAt = row.EndsAt
			}
		};
	}

	/// <summary>A stored row of the <c>restrictions</c> table.</summary>
	private sealed class RestrictionRow
	{
		public int Id { get; set; }
		public int UserId { get; set; }
		public long Permissions { get; set; }
		public DateTimeOffset StartsAt { get; set; }
		public DateTimeOffset? EndsAt { get; set; }
	}
}