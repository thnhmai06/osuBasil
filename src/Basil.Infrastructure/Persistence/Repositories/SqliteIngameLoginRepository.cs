using Basil.Application.Abstractions.Users;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IIngameLoginRepository" />
/// <remarks>
///     Rows map through the private mutable <c>IngameLoginRow</c> DTO, since Dapper fills by
///     property name rather than through a positional record constructor.
/// </remarks>
public sealed class SqliteIngameLoginRepository(string connectionString, ILogger<SqliteIngameLoginRepository> logger)
	: IIngameLoginRepository
{
	/// <inheritdoc />
	/// <remarks>
	///     The <see cref="DateOnly" /> client version is stored as a <c>datetime</c> at midnight UTC
	///     and converted back to a <see cref="DateOnly" /> when read. The insert and the id read-back
	///     are one batched statement, so the auto-increment id is the immediately inserted row's.
	/// </remarks>
	public async Task<IngameLogin> CreateAsync(int userId, string ip, DateOnly osuVer, string osuStream,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var id = await connection.ExecuteScalarAsync<int>(
			"""
			INSERT INTO IngameLogins (UserId, Ip, OsuVer, OsuStream, LoggedInAt)
			VALUES (@UserId, @Ip, @OsuVer, @OsuStream, datetime('now'));
			SELECT last_insert_rowid();
			""",
			new { UserId = userId, Ip = ip, OsuVer = osuVer.ToDateTime(TimeOnly.MinValue), OsuStream = osuStream });
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
		public DateTime OsuVer { get; set; }
		public string OsuStream { get; set; } = "";
		public DateTime LoggedInAt { get; set; }

		/// <summary>
		///     Builds an <see cref="IngameLogin" /> from this row, converting the stored version
		///     date back to a <see cref="DateOnly" />.
		/// </summary>
		/// <returns>The domain ingame login record.</returns>
		public IngameLogin ToIngameLogin()
		{
			return new IngameLogin(Id, UserId, Ip, DateOnly.FromDateTime(OsuVer), OsuStream, LoggedInAt);
		}
	}
}