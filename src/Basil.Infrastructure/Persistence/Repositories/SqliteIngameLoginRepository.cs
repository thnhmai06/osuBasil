using Basil.Application.Abstractions.Login;
using Basil.Domain.Login;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="ILoginRepository" />
/// <remarks>
///     Rows map through the private mutable <c>Login</c> DTO, since Dapper fills by
///     property name rather than through a positional record constructor.
/// </remarks>
public sealed class SqliteLoginRepository(string connectionString, ILogger<SqliteLoginRepository> logger)
	: ILoginRepository
{
	/// <inheritdoc />
	/// <remarks>
	///     The <see cref="DateOnly" /> client version is stored as a <c>datetime</c> at midnight UTC
	///     and converted back to a <see cref="DateOnly" /> when read. The insert and the id read-back
	///     are one batched statement, so the auto-increment id is the immediately inserted row's.
	/// </remarks>
	public async Task<Login> CreateAsync(int userId, string ip, DateOnly osuVersion, string osuStream,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var id = await connection.ExecuteScalarAsync<int>(
			"""
			INSERT INTO IngameLogins (UserId, Ip, OsuVersion, OsuStream, LoggedInAt)
			VALUES (@UserId, @Ip, @OsuVersion, @OsuStream, datetime('now'));
			SELECT last_insert_rowid();
			""",
			new { UserId = userId, Ip = ip, OsuVersion = osuVersion.ToDateTime(TimeOnly.MinValue), OsuStream = osuStream });
		logger.LogDebug("IngameLogin created for UserId={UserId}", userId);

		var row = await connection.QuerySingleAsync<IngameLoginRow>(
			"SELECT * FROM IngameLogins WHERE Id = @Id",
			new { Id = id });

		return row.ToIngameLogin();
	}

	/// <summary>Creates a new SQLite connection using the repository's connection string.</summary>
	private SqliteConnection Connect()
	{
		return new SqliteConnection(connectionString);
	}

	/// <summary>
	///     A mutable row DTO matching the IngameLogins table columns. Mutable because Dapper fills
	///     by property name, not through a positional record constructor.
	/// </summary>
	private sealed class IngameLoginRow
	{
		public int Id { get; set; }
		public int UserId { get; set; }
		public string Ip { get; set; } = "";
		public DateTime OsuVersion { get; set; }
		public string OsuStream { get; set; } = "";
		public DateTime LoggedInAt { get; set; }

		/// <summary>
		///     Builds an <see cref="Login" /> from this row, converting the stored version
		///     date back to a <see cref="DateOnly" />.
		/// </summary>
		/// <returns>The domain ingame login record.</returns>
		public Login ToIngameLogin()
		{
			return new Login(Id, UserId, Ip, DateOnly.FromDateTime(OsuVersion), OsuStream, LoggedInAt);
		}
	}
}