using System.Collections.Immutable;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Infrastructure.Common.Persistence;
using Basil.Infrastructure.Common.Registry;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Users.Persistence;

/// <summary>Stores the restrictions of users.</summary>
internal sealed class PostgresRestrictionRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	IUserRepository users)
	: MemoryRepository<int, UserRestriction>(reader, writer), IRestrictionRepository
{
	private readonly OwnedLists<int, UserRecord, UserRestriction> _byUser = new();

	/// <inheritdoc />
	public async Task<UserRestriction> CreateAsync(RestrictionData data,
		CancellationToken cancellationToken = default)
	{
		var user = await users.GetAsync(data.User.Id, cancellationToken) ?? data.User;
		var value = new RestrictionData
		{
			User = user,
			Permissions = data.Permissions,
			StartsAt = data.StartsAt,
			EndsAt = data.EndsAt
		};

		var id = await Writer.WriteAsync(Root.User(user.Id), async (UsersDbContext context, CancellationToken token) =>
		{
			var row = new RestrictionRow
			{
				UserId = value.User.Id,
				Permissions = (long)value.Permissions,
				StartsAt = value.StartsAt.ToUniversalTime(),
				EndsAt = value.EndsAt?.ToUniversalTime()
			};
			context.Restrictions.Add(row);
			await context.SaveChangesAsync(token);
			return row.Id;
		}, cancellationToken);

		var restriction = Track(new UserRestriction { Id = id, Value = value });
		_byUser.Change(user.Id, user, current => Replace(current, restriction), Task.CompletedTask);
		return restriction;
	}

	/// <inheritdoc />
	public Task UpdateAsync(UserRestriction restriction, CancellationToken cancellationToken = default)
	{
		var saved = SaveAsync(restriction);
		var live = Track(restriction);
		_byUser.Change(live.Value.User.Id, live.Value.User, current => Replace(current, live), saved);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task DeleteAsync(UserRestriction restriction, CancellationToken cancellationToken = default)
	{
		var user = restriction.Value.User;
		var removed = await Writer.WriteAsync<UsersDbContext, int>(Root.User(user.Id),
			(context, token) => context.Restrictions.Where(row => row.Id == restriction.Id)
				.ExecuteDeleteAsync(token), cancellationToken);
		if (removed == 0) return;

		Items.Remove(restriction.Id);
		_byUser.Change(user.Id, user,
			current => current.RemoveAll(item => item.Id == restriction.Id), Task.CompletedTask);
	}

	/// <inheritdoc />
	public ValueTask<UserRestriction?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		return FindAsync(id, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<UserRestriction>> ListAsync(UserRecord user,
		CancellationToken cancellationToken = default)
	{
		var liveUser = await users.GetAsync(user.Id, cancellationToken) ?? user;
		return await _byUser.GetOrLoadAsync(liveUser.Id, liveUser, async () =>
		{
			var rows = await Reader.ReadAsync((UsersDbContext context) => context.Restrictions
				.Where(row => row.UserId == liveUser.Id)
				.OrderBy(row => row.StartsAt)
				.ThenBy(row => row.Id)
				.ToListAsync(cancellationToken), cancellationToken);
			var restrictions = ImmutableList.CreateBuilder<UserRestriction>();
			foreach (var row in rows)
				restrictions.Add(Track(ToRestriction(row, liveUser)));
			return restrictions.ToImmutable();
		});
	}

	protected override int KeyOf(UserRestriction item)
	{
		return item.Id;
	}

	protected override Root RootOf(UserRestriction item)
	{
		return Root.User(item.Value.User.Id);
	}

	protected override void CopyTo(UserRestriction live, UserRestriction from)
	{
		live.Value.Permissions = from.Value.Permissions;
		live.Value.EndsAt = from.Value.EndsAt;
	}

	protected override async Task<UserRestriction?> LoadAsync(int key)
	{
		var row = await Reader.ReadAsync((UsersDbContext context) => context.Restrictions
			.Where(row => row.Id == key)
			.FirstOrDefaultAsync(CancellationToken.None));
		if (row is null)
			return null;

		var user = await users.GetAsync(row.UserId);
		return user is null ? null : ToRestriction(row, user);
	}

	protected override WriteCommand<UsersDbContext> SaveCommand(UserRestriction restriction)
	{
		var row = ToRow(restriction);
		return new WriteCommand<UsersDbContext>((context, token) =>
			context.ReplaceAsync(row, stored => stored.Id == row.Id, token));
	}

	private static ImmutableList<UserRestriction> Replace(ImmutableList<UserRestriction> current,
		UserRestriction item)
	{
		var index = current.FindIndex(restriction => restriction.Id == item.Id);
		return index < 0 ? current.Add(item) : current.SetItem(index, item);
	}

	/// <summary>Makes the row that stores a restriction, with the values it has now.</summary>
	private static RestrictionRow ToRow(UserRestriction restriction)
	{
		return new RestrictionRow
		{
			Id = restriction.Id,
			UserId = restriction.Value.User.Id,
			Permissions = (long)restriction.Value.Permissions,
			StartsAt = restriction.Value.StartsAt.ToUniversalTime(),
			EndsAt = restriction.Value.EndsAt?.ToUniversalTime()
		};
	}

	/// <summary>Builds a restriction from a stored row.</summary>
	private static UserRestriction ToRestriction(RestrictionRow row, UserRecord user)
	{
		return new UserRestriction
		{
			Id = row.Id,
			Value = new RestrictionData
			{
				User = user,
				Permissions = (UserPermissions)(ulong)row.Permissions,
				StartsAt = row.StartsAt,
				EndsAt = row.EndsAt
			}
		};
	}
}