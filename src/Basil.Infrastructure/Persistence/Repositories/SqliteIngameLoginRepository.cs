using Basil.Application.Abstractions.Login;
using Basil.Domain.Login;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="ILoginRepository" />
/// <remarks>
///     Rows map through the private mutable <c>IngameLoginRow</c> DTO.
/// </remarks>
public sealed class SqliteLoginRepository(string connectionString, ILogger<SqliteLoginRepository> logger)
	: ILoginRepository
{
	/// <inheritdoc />
	/// <remarks>
	///     The osu! client's <see cref="DateOnly" /> version is stored as a <c>datetime</c> at midnight
	///     UTC and converted back to <see cref="DateOnly" /> on read. The insert and the id read-back
	///     are one batched statement, so the returned id is always the row just inserted.
	/// </remarks>
	public async Task<Login> CreateAsync(int userId, string ip, DateOnly osuVersion, string osuStream,
		CancellationToken cancellationToken = default)
	{
		return await SqliteInstrumentation.RecordAsync("login.create", async () =>
		{
			await using var connection = Connect();
			// RETURNING folds the id read-back into the insert itself — one write round trip instead
			// of two, halving this call's contribution to write contention (see ADR-001).
			var row = await connection.QuerySingleAsync<IngameLoginRow>(
				"""
				INSERT INTO IngameLogins (UserId, Ip, OsuVersion, OsuStream, LoggedInAt)
				VALUES (@UserId, @Ip, @OsuVersion, @OsuStream, datetime('now'))
				RETURNING *;
				""",
				new
				{
					UserId = userId, Ip = ip, OsuVersion = osuVersion.ToDateTime(TimeOnly.MinValue),
					OsuStream = osuStream
				});
			logger.LogDebug("IngameLogin created for UserId={UserId}", userId);

			return row.ToIngameLogin();
		});
	}

	/// <summary>Creates a new SQLite connection using the repository's connection string.</summary>
	private SqliteConnection Connect()
	{
		return SqliteConnectionFactory.Open(connectionString);
	}

	/// <summary>
	///     A mutable row DTO matching the IngameLogins table columns.
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