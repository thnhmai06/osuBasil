using Basil.Application.Abstractions.Users;
using Basil.Domain.Users;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IClientHashRepository" />
/// <remarks>
///     Rows map through the private mutable <c>ClientHashRow</c> and <c>ClientHashWithPlayerRow</c>
///     DTOs. Each method opens its own connection.
/// </remarks>
public sealed class SqliteClientHashRepository(string connectionString, ILogger<SqliteClientHashRepository> logger)
	: IClientHashRepository
{
	/// <inheritdoc />
	/// <remarks>
	///     An upsert keyed on the full fingerprint. The first insert creates a row with one
	///     occurrence; a later login with the same fingerprint bumps <c>Occurrences</c> and
	///     refreshes <c>LastSeenAt</c>. The row is then re-read by that fingerprint and returned.
	/// </remarks>
	public async Task<ClientHash> CreateAsync(int userId, string osuPathMd5, string adapters, string uninstallId,
		string diskSerial, CancellationToken cancellationToken = default)
	{
		return await SqliteInstrumentation.RecordAsync("clienthash.create", async () =>
		{
			await using var connection = Connect();
			// RETURNING folds the re-read into the upsert itself — one write round trip instead of
			// two, halving this call's contribution to write contention (see ADR-001).
			var row = await connection.QuerySingleAsync<ClientHashRow>(
				"""
				INSERT INTO ClientHashes (UserId, OsuPathMd5, Adapters, UninstallId, DiskSerial, LastSeenAt, Occurrences)
				VALUES (@UserId, @OsuPathMd5, @Adapters, @UninstallId, @DiskSerial, datetime('now'), 1)
				ON CONFLICT (UserId, OsuPathMd5, Adapters, UninstallId, DiskSerial)
				DO UPDATE SET LastSeenAt = datetime('now'), Occurrences = Occurrences + 1
				RETURNING *;
				""",
				new
				{
					UserId = userId,
					OsuPathMd5 = osuPathMd5,
					Adapters = adapters,
					UninstallId = uninstallId,
					DiskSerial = diskSerial
				});
			logger.LogDebug("ClientHash upserted for UserId={UserId}", userId);

			return row.ToClientHash();
		});
	}

	/// <inheritdoc />
	/// <remarks>
	///     Under Wine only the uninstall id is compared, since adapter and disk fingerprints are
	///     unreliable there. Otherwise, any match on adapters, uninstall id, or disk serial (when
	///     supplied) counts as shared hardware. The query joins the Users table, so each result
	///     carries the other account's name and privileges and always excludes
	///     <paramref name="userId" /> itself.
	/// </remarks>
	public async Task<IReadOnlyList<PlayerClientHash>> FetchAnyHardwareMatchesForUserAsync(
		int userId,
		bool runningUnderWine,
		string adapters,
		string uninstallId,
		string? diskSerial,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();

		var sql = """
		          SELECT ch.UserId, ch.OsuPathMd5, ch.Adapters, ch.UninstallId,
		                 ch.DiskSerial, ch.LastSeenAt, ch.Occurrences,
		                 u.Name, u.Privilege
		          FROM ClientHashes ch
		          JOIN Users u ON ch.UserId = u.Id
		          WHERE ch.UserId != @UserId
		          """;

		if (runningUnderWine)
		{
			sql += " AND ch.UninstallId = @UninstallId";
		}
		else
		{
			var oneOf = new List<string> { "ch.Adapters = @Adapters", "ch.UninstallId = @UninstallId" };
			if (diskSerial is not null) oneOf.Add("ch.DiskSerial = @DiskSerial");

			sql += $" AND ({string.Join(" OR ", oneOf)})";
		}

		var rows = await connection.QueryAsync<ClientHashWithPlayerRow>(
			sql,
			new { UserId = userId, Adapters = adapters, UninstallId = uninstallId, DiskSerial = diskSerial });

		return [.. rows.Select(r => r.ToClientHashWithPlayer())];
	}

	/// <summary>Creates a new SQLite connection using the repository's connection string.</summary>
	private SqliteConnection Connect()
	{
		return SqliteConnectionFactory.Open(connectionString);
	}

	/// <summary>
	///     A mutable row DTO matching the ClientHashes table columns.
	/// </summary>
	private sealed class ClientHashRow
	{
		public int UserId { get; set; }
		public string OsuPathMd5 { get; set; } = "";
		public string Adapters { get; set; } = "";
		public string UninstallId { get; set; } = "";
		public string DiskSerial { get; set; } = "";
		public DateTime LastSeenAt { get; set; }
		public int Occurrences { get; set; }

		/// <summary>Builds a <see cref="ClientHash" /> from this row.</summary>
		/// <returns>The domain client hash record.</returns>
		public ClientHash ToClientHash()
		{
			return new ClientHash(UserId, OsuPathMd5, Adapters, UninstallId, DiskSerial, LastSeenAt, Occurrences);
		}
	}

	/// <summary>
	///     A mutable row DTO matching the ClientHashes table columns joined with the owning user's
	///     name and privileges.
	/// </summary>
	private sealed class ClientHashWithPlayerRow
	{
		public int UserId { get; set; }
		public string OsuPathMd5 { get; set; } = "";
		public string Adapters { get; set; } = "";
		public string UninstallId { get; set; } = "";
		public string DiskSerial { get; set; } = "";
		public DateTime LastSeenAt { get; set; }
		public int Occurrences { get; set; }
		public string Name { get; set; } = "";
		public int Privilege { get; set; }

		/// <summary>
		///     Builds a <see cref="PlayerClientHash" /> from this row, casting the stored
		///     privilege column.
		/// </summary>
		/// <returns>The domain client hash-with-player record.</returns>
		public PlayerClientHash ToClientHashWithPlayer()
		{
			return new PlayerClientHash(
				UserId, OsuPathMd5, Adapters, UninstallId, DiskSerial, LastSeenAt,
				Occurrences, Name, (UserPrivileges)Privilege);
		}
	}
}