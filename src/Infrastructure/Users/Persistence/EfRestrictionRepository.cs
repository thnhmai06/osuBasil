using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Users.Persistence;

/// <summary>Stores the restrictions of users.</summary>
internal sealed class EfRestrictionRepository(DatabaseSession database) : IRestrictionRepository
{
	/// <inheritdoc />
	public async Task<UserRestriction> CreateAsync(RestrictionData data,
		CancellationToken cancellationToken = default)
	{
		var row = new RestrictionRow
		{
			UserId = data.User.Id,
			Permissions = (long)data.Permissions,
			StartsAt = data.StartsAt.ToUniversalTime(),
			EndsAt = data.EndsAt?.ToUniversalTime()
		};
		await database.WriteAsync((UsersDbContext context, CancellationToken _) =>
		{
			context.Restrictions.Add(row);
			return Task.CompletedTask;
		}, cancellationToken);

		return new UserRestriction { Id = row.Id, Value = data };
	}

	/// <inheritdoc />
	public Task UpdateAsync(UserRestriction restriction, CancellationToken cancellationToken = default)
	{
		var id = restriction.Id;
		var permissions = (long)restriction.Value.Permissions;
		var endsAt = restriction.Value.EndsAt?.ToUniversalTime();
		return database.WriteAsync((UsersDbContext context, CancellationToken token) => context.Restrictions
			.Where(row => row.Id == id)
			.ExecuteUpdateAsync(set => set
				.SetProperty(row => row.Permissions, permissions)
				.SetProperty(row => row.EndsAt, endsAt), token), cancellationToken);
	}

	/// <inheritdoc />
	public Task DeleteAsync(UserRestriction restriction, CancellationToken cancellationToken = default)
	{
		var id = restriction.Id;
		return database.WriteAsync((UsersDbContext context, CancellationToken token) => context.Restrictions
			.Where(row => row.Id == id)
			.ExecuteDeleteAsync(token), cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask<UserRestriction?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		var found = await database.ReadAsync((UsersDbContext context) =>
			(from row in context.Restrictions
				where row.Id == id
				join user in context.Users on row.UserId equals user.Id
				select new { row, user }).FirstOrDefaultAsync(cancellationToken), cancellationToken);
		return found is null ? null : ToRestriction(found.row, EfUserRepository.ToUser(found.user));
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<UserRestriction>> ListAsync(UserRecord user,
		CancellationToken cancellationToken = default)
	{
		var userId = user.Id;
		var rows = await database.ReadAsync((UsersDbContext context) => context.Restrictions
			.Where(row => row.UserId == userId)
			.OrderBy(row => row.StartsAt)
			.ThenBy(row => row.Id)
			.ToListAsync(cancellationToken), cancellationToken);
		return [.. rows.Select(row => ToRestriction(row, user))];
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