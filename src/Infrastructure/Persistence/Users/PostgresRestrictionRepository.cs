using System.Collections.Immutable;
using Basil.Application.Persistence.Users;
using Basil.Domain.Permissions;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Postgres;
using Basil.Infrastructure.Persistence.Memory;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Users;

/// <summary>Stores the restrictions of users.</summary>
internal sealed class PostgresRestrictionRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	IUserRepository users)
	: MemoryRepository<int, Restriction>(reader, writer), IRestrictionRepository
{
	private readonly OwnedLists<int, UserRecord, Restriction> _byUser = new();

	/// <inheritdoc />
	public async Task<Restriction> CreateAsync(RestrictionData data,
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

		var id = await Writer.WriteAsync(Root.User(user.Id), async (context, token) =>
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

		var restriction = Track(new Restriction { Id = id, Value = value });
		_byUser.Change(user.Id, user, current => Replace(current, restriction), Task.CompletedTask);
		return restriction;
	}

	/// <inheritdoc />
	public Task UpdateAsync(Restriction restriction, CancellationToken cancellationToken = default)
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
	public async Task<IReadOnlyList<Restriction>> ListAsync(UserRecord user,
		CancellationToken cancellationToken = default)
	{
		var liveUser = await users.GetAsync(user.Id, cancellationToken) ?? user;
		return await _byUser.GetOrLoadAsync(liveUser.Id, liveUser, async () =>
		{
			var rows = await Reader.ReadAsync(context => context.Restrictions
				.Where(row => row.UserId == liveUser.Id)
				.OrderBy(row => row.StartsAt)
				.ThenBy(row => row.Id)
				.ToListAsync(cancellationToken), cancellationToken);
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
		var row = await Reader.ReadAsync(context => context.Restrictions
			.Where(row => row.Id == key)
			.FirstOrDefaultAsync(cancellationToken: CancellationToken.None));
		if (row is null)
			return null;

		var user = await users.GetAsync(row.UserId);
		return user is null ? null : ToRestriction(row, user);
	}

	protected override WriteCommand SaveCommand(Restriction restriction)
	{
		var row = ToRow(restriction);
		return new WriteCommand((context, token) => context.ReplaceAsync(row, stored => stored.Id == row.Id, token));
	}

	private static ImmutableList<Restriction> Replace(ImmutableList<Restriction> current,
		Restriction item)
	{
		var index = current.FindIndex(restriction => restriction.Id == item.Id);
		return index < 0 ? current.Add(item) : current.SetItem(index, item);
	}

	/// <summary>Makes the row that stores a restriction, with the values it has now.</summary>
	private static RestrictionRow ToRow(Restriction restriction)
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
	private static Restriction ToRestriction(RestrictionRow row, UserRecord user)
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
}