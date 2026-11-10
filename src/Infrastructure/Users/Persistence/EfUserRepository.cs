using System.Globalization;
using Basil.Application.Common;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Users.Persistence;

/// <summary>Stores registered users.</summary>
/// <remarks>Every read goes to the database: the online users are kept by the user registry, not here.</remarks>
internal sealed class EfUserRepository(DatabaseSession database) : IUserRepository
{
	/// <inheritdoc />
	public async Task<UserRecord> CreateAsync(UserData data, CancellationToken cancellationToken = default)
	{
		var row = ToRow(0, data);
		try
		{
			await database.WriteAsync(async (UsersDbContext context, CancellationToken token) =>
			{
				row.Id = (int)await IdCounters.NextAsync(context, "user", token);
				context.Users.Add(row);
			}, cancellationToken);
		}
		catch (AlreadyExistsException)
		{
			throw AlreadyNamed(data.Name);
		}

		return new UserRecord { Id = row.Id, Value = data };
	}

	/// <inheritdoc />
	public async Task<UserRecord> ReplaceAsync(int id, UserData data, CancellationToken cancellationToken = default)
	{
		var row = ToRow(id, data);
		try
		{
			await database.WriteAsync((UsersDbContext context, CancellationToken token) =>
				context.UpsertAsync(row, stored => stored.Id == id, token), cancellationToken);
		}
		catch (AlreadyExistsException)
		{
			throw AlreadyNamed(data.Name);
		}

		return new UserRecord { Id = id, Value = data };
	}

	/// <inheritdoc />
	public Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default)
	{
		var id = user.Id;
		var country = (int)user.Value.Country;
		var privileges = (int)user.Value.Privileges;
		var silencedUntil = user.Value.SilencedUntil?.ToUniversalTime();
		var deletedAt = user.Value.DeletedAt?.ToUniversalTime();
		// A deletion is permanent: a record that does not know it must not take it back.
		return database.WriteAsync((UsersDbContext context, CancellationToken token) => context.Users
				.Where(row => row.Id == id)
				.ExecuteUpdateAsync(set => set
					.SetProperty(row => row.Country, country)
					.SetProperty(row => row.Privileges, privileges)
					.SetProperty(row => row.SilencedUntil, silencedUntil)
					.SetProperty(row => row.DeletedAt, row => row.DeletedAt ?? deletedAt)
					.SetProperty(row => row.ActiveName,
						row => row.DeletedAt == null && deletedAt == null ? row.NormalizedName : null), token),
			cancellationToken);
	}

	/// <inheritdoc />
	public Task DiscardAsync(UserRecord user, CancellationToken cancellationToken = default)
	{
		var id = user.Id;
		return database.WriteAsync((UsersDbContext context, CancellationToken token) => context.Users
			.Where(row => row.Id == id)
			.ExecuteDeleteAsync(token), cancellationToken);
	}

	/// <inheritdoc />
	public async Task<bool> RenameAsync(UserRecord user, string name, CancellationToken cancellationToken = default)
	{
		_ = new UserData { Name = name };
		var id = user.Id;
		var normalized = UserData.NormalizeName(name);

		// Whether the name is free is the database's to say, so the record takes the name only once it is committed.
		try
		{
			await database.WriteAsync((UsersDbContext context, CancellationToken token) => context.Users
					.Where(row => row.Id == id)
					.ExecuteUpdateAsync(set => set
						.SetProperty(row => row.Name, name)
						.SetProperty(row => row.NormalizedName, normalized)
						.SetProperty(row => row.ActiveName, row => row.DeletedAt == null ? normalized : null), token),
				cancellationToken);
		}
		catch (AlreadyExistsException)
		{
			return false;
		}

		user.Value.Name = name;
		return true;
	}

	/// <inheritdoc />
	public async ValueTask<UserRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		var row = await database.ReadAsync((UsersDbContext context) => context.Users
			.Where(candidate => candidate.Id == id)
			.FirstOrDefaultAsync(cancellationToken), cancellationToken);
		return row is null ? null : ToUser(row);
	}

	/// <inheritdoc />
	public async ValueTask<UserRecord?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
	{
		var normalized = UserData.NormalizeName(name);
		var row = await database.ReadAsync((UsersDbContext context) => context.Users
			.Where(candidate => candidate.NormalizedName == normalized && candidate.DeletedAt == null)
			.FirstOrDefaultAsync(cancellationToken), cancellationToken);
		return row is null ? null : ToUser(row);
	}

	/// <inheritdoc />
	public async Task<Page<UserRecord>> ListAsync(UserQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var fetched = await database.ReadAsync((UsersDbContext context) => Filter(context.Users, query)
			.OrderBy(row => row.Id).Skip(page.Offset).Take(page.Fetch)
			.ToListAsync(cancellationToken), cancellationToken);

		var (rows, hasNext) = page.Slice(fetched);
		return new Page<UserRecord>([.. rows.Select(ToUser)], hasNext);
	}

	/// <summary>Narrows the users to the ones a query asks for.</summary>
	private static IQueryable<UserRow> Filter(IQueryable<UserRow> users, UserQuery query)
	{
		if (query.Deleted is { } deleted)
			users = deleted ? users.Where(row => row.DeletedAt != null) : users.Where(row => row.DeletedAt == null);

		if (query.Countries is { Count: > 0 } countries)
		{
			var codes = countries.Select(country => (int)country).ToList();
			users = users.Where(row => codes.Contains(row.Country));
		}

		if (query.Privileges is { } privileges)
		{
			var mask = (int)privileges;
			users = users.Where(row => (row.Privileges & mask) == mask);
		}

		if (query.Text is { } text)
		{
			var pattern = LikePattern.Containing(text.ToLowerInvariant().Replace(' ', '_'));
			users = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
				? users.Where(row => row.Id == id || EF.Functions.Like(row.NormalizedName, pattern, LikePattern.Escape))
				: users.Where(row => EF.Functions.Like(row.NormalizedName, pattern, LikePattern.Escape));
		}

		return users;
	}

	/// <summary>Builds the row of an account; an id of zero stands for an account that has none yet.</summary>
	/// <param name="id">The id of the account the row stores.</param>
	/// <param name="data">The data of the account.</param>
	/// <returns>The row to store.</returns>
	private static UserRow ToRow(int id, UserData data)
	{
		return new UserRow
		{
			Id = id,
			Name = data.Name,
			NormalizedName = data.NormalizedName,
			ActiveName = data.DeletedAt is null ? data.NormalizedName : null,
			Country = (int)data.Country,
			Privileges = (int)data.Privileges,
			SilencedUntil = data.SilencedUntil?.ToUniversalTime(),
			DeletedAt = data.DeletedAt?.ToUniversalTime()
		};
	}

	/// <summary>Builds the refusal for a name that another account already holds.</summary>
	/// <param name="name">The name that is taken.</param>
	/// <returns>The refusal to throw.</returns>
	private static AlreadyExistsException AlreadyNamed(string name)
	{
		return new AlreadyExistsException($"A user is already named {name}.");
	}

	/// <summary>Builds a user from a stored row.</summary>
	internal static UserRecord ToUser(UserRow row)
	{
		return new UserRecord
		{
			Id = row.Id,
			Value = new UserData
			{
				Name = row.Name,
				Country = (Country)row.Country,
				Privileges = (UserPrivileges)row.Privileges,
				SilencedUntil = row.SilencedUntil,
				DeletedAt = row.DeletedAt
			}
		};
	}
}