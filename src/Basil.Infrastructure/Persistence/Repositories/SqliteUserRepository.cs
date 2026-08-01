using Basil.Application.Abstractions.Users;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IUserRepository" />
/// <remarks>
///     Rows map through the private mutable <c>UserRow</c> DTO, since Dapper fills by property
///     name rather than through a positional record constructor. Each method opens its own
///     connection.
/// </remarks>
public sealed class SqliteUserRepository(string connectionString, ILogger<SqliteUserRepository> logger)
	: IUserRepository
{
	/// <inheritdoc />
	public async Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
			"SELECT * FROM Users WHERE Id = @Id",
			new { Id = id });
		return row?.ToUser();
	}

	/// <inheritdoc />
	/// <remarks>
	///     The lookup is against the <c>SafeName</c> column, populated via
	///     <see cref="User.MakeSafeName" />, so a name matches regardless of case or spaces.
	/// </remarks>
	public async Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
			"SELECT * FROM Users WHERE SafeName = @SafeName",
			new { SafeName = User.MakeSafeName(name) });
		return row?.ToUser();
	}

	/// <inheritdoc />
	public async Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		return await connection.QuerySingleOrDefaultAsync<string>(
			"SELECT PwBcrypt FROM Users WHERE Id = @Id",
			new { Id = id });
	}

	/// <inheritdoc />
	/// <remarks>
	///     The country is stored as its two-letter acronym, produced by the
	///     <c>Country.ToAcronym()</c> extension.
	/// </remarks>
	public async Task UpdateCountryAsync(int id, Country country, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync(
			"UPDATE Users SET Country = @Country WHERE Id = @Id",
			new { Id = id, Country = country.ToAcronym() });
		logger.LogDebug("User row updated: Id={Id} Country={Country}", id, country);
	}

	/// <inheritdoc />
	public async Task UpdatePrivilegesAsync(int id, UserPrivileges privilege,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync(
			"UPDATE Users SET Privilege = @Privilege WHERE Id = @Id",
			new { Id = id, Privilege = (int)privilege });
		logger.LogDebug("User row updated: Id={Id} Privilege={Privilege}", id, privilege);
	}

	/// <inheritdoc />
	public async Task UpdateNameAsync(int id, string name, string safeName,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync(
			"UPDATE Users SET Name = @Name, SafeName = @SafeName WHERE Id = @Id",
			new { Id = id, Name = name, SafeName = safeName });
		logger.LogDebug("User row updated: Id={Id} Name={Name}", id, name);
	}

	/// <inheritdoc />
	/// <remarks>
	///     The safe form of the name is derived via <see cref="User.MakeSafeName" />, and the
	///     default privilege set, when <paramref name="privilege" /> is <see langword="null" />, is
	///     the unrestricted, verified, and supporter flags. A duplicate display or safe name
	///     surfaces as a SQLite constraint violation (error code 19), which is swallowed and
	///     reported as <see langword="null" />. The insert and the id read-back are one batched
	///     statement, and the new row is then re-read and returned.
	/// </remarks>
	public async Task<User?> CreateAsync(string name, string pwBcrypt, Country country,
		UserPrivileges? privilege = null,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		int id;
		try
		{
			id = await connection.ExecuteScalarAsync<int>(
				"""
				INSERT INTO Users (Name, SafeName, PwBcrypt, Country, Privilege)
				VALUES (@Name, @SafeName, @PwBcrypt, @Country, @Privilege);
				SELECT last_insert_rowid();
				""",
				new
				{
					Name = name,
					SafeName = User.MakeSafeName(name),
					PwBcrypt = pwBcrypt,
					Country = country.ToAcronym(),
					Privilege = (int)(privilege ?? UserPrivileges.Unrestricted | UserPrivileges.Verified |
						UserPrivileges.Supporter)
				});
		}
		catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT (Name/SafeName UNIQUE)
		{
			return null;
		}

		logger.LogDebug("User row created: Id={Id} Name={Name}", id, name);
		return (await FetchByIdAsync(id, cancellationToken))!;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<User>> FetchAllAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var rows = await connection.QueryAsync<UserRow>("SELECT * FROM Users ORDER BY Id");
		return [.. rows.Select(r => r.ToUser())];
	}

	/// <summary>Creates a new SQLite connection using the repository's connection string.</summary>
	private SqliteConnection Connect()
	{
		return new SqliteConnection(connectionString);
	}

	/// <summary>
	///     A mutable row DTO matching the Users table columns. Mutable because Dapper fills by
	///     property name (coercing column types loosely) rather than through a positional record
	///     constructor.
	/// </summary>
	private sealed class UserRow
	{
		public int Id { get; set; }
		public string Name { get; set; } = "";
		public string SafeName { get; set; } = "";
		public int Privilege { get; set; }
		public string Country { get; set; } = "";
		public DateTime SilenceEnd { get; set; }

		/// <summary>Builds a <see cref="User" /> from this row.</summary>
		/// <returns>The domain user.</returns>
		/// <remarks>
		///     The stored country acronym is parsed back to a <see cref="Country" /> value, falling
		///     back to <see cref="Country.Xx" /> when unrecognized, and the silence-end time is
		///     reinterpreted as UTC before being exposed as an offset.
		/// </remarks>
		public User ToUser()
		{
			var country = Enum.TryParse<Country>(Country, true, out var parsed)
				? parsed
				: Domain.Login.Country.Xx;
			return new User(Id, Name, country, (UserPrivileges)Privilege,
				new DateTimeOffset(DateTime.SpecifyKind(SilenceEnd, DateTimeKind.Utc)));
		}
	}
}