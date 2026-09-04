using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IBeatmapsetRepository" />
/// <remarks>
///     Rows map through the private mutable <c>BeatmapsetRow</c> DTO. Each method opens its own
///     connection.
/// </remarks>
public sealed class SqliteBeatmapsetRepository(string connectionString, ILogger<SqliteBeatmapsetRepository> logger)
	: IBeatmapsetRepository
{
	/// <inheritdoc />
	public async Task<Beatmapset?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var row = await connection.QuerySingleOrDefaultAsync<BeatmapsetRow>(
			"SELECT * FROM Beatmapsets WHERE Id = @Id", new { Id = id });
		return row?.ToBeatmapset();
	}

	/// <inheritdoc />
	/// <remarks>
	///     The update clause overwrites only the shared metadata columns.
	///     <see cref="Beatmapset.IsFrozen" />, <see cref="Beatmapset.IsPrivate" />, and the
	///     media-file columns are deliberately left alone: a re-ingestion pass must never clear an
	///     admin-set freeze lock or privacy flag, and the pass's actual background and audio files
	///     are not known yet (they are set later via <see cref="SetBackgroundFileAsync" /> and
	///     <see cref="SetAudioFileAsync" />). The persisted row is returned by the same statement
	///     that writes it (<c>RETURNING</c>), not a separate follow-up read, so a concurrent
	///     <see cref="DeleteAsync" /> can never land in the gap between the write and the read.
	/// </remarks>
	public async Task<Beatmapset> UpsertAsync(Beatmapset beatmapset, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var row = await connection.QuerySingleAsync<BeatmapsetRow>(
			"""
			INSERT INTO Beatmapsets (Id, Artist, Title, Creator, LastUpdate, CreatedAt, IsFrozen, IsPrivate, BackgroundFile, AudioFile)
			VALUES (@Id, @Artist, @Title, @Creator, @LastUpdate, @CreatedAt, @IsFrozen, @IsPrivate, @BackgroundFile, @AudioFile)
			ON CONFLICT(Id) DO UPDATE SET
			    Artist = excluded.Artist, Title = excluded.Title, Creator = excluded.Creator,
			    LastUpdate = excluded.LastUpdate, CreatedAt = excluded.CreatedAt
			RETURNING *
			""",
			new
			{
				beatmapset.Id,
				beatmapset.Artist,
				beatmapset.Title,
				beatmapset.Creator,
				beatmapset.LastUpdate,
				beatmapset.CreatedAt,
				beatmapset.IsFrozen,
				beatmapset.IsPrivate,
				beatmapset.BackgroundFile,
				beatmapset.AudioFile
			});
		logger.LogDebug("Beatmapset upserted: Id={Id}", beatmapset.Id);

		return row.ToBeatmapset();
	}

	/// <inheritdoc />
	public async Task SetFrozenAsync(int id, bool frozen, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync("UPDATE Beatmapsets SET IsFrozen = @Frozen WHERE Id = @Id",
			new { Id = id, Frozen = frozen });
		logger.LogDebug("Beatmapset frozen flag set: Id={Id} Frozen={Frozen}", id, frozen);
	}

	/// <inheritdoc />
	public async Task SetPrivateAsync(int id, bool isPrivate, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync("UPDATE Beatmapsets SET IsPrivate = @IsPrivate WHERE Id = @Id",
			new { Id = id, IsPrivate = isPrivate });
		logger.LogDebug("Beatmapset private flag set: Id={Id} IsPrivate={IsPrivate}", id, isPrivate);
	}

	/// <inheritdoc />
	public async Task SetBackgroundFileAsync(int id, string? backgroundFile,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync("UPDATE Beatmapsets SET BackgroundFile = @BackgroundFile WHERE Id = @Id",
			new { Id = id, BackgroundFile = backgroundFile });
		logger.LogDebug("Beatmapset background file set: Id={Id}", id);
	}

	/// <inheritdoc />
	public async Task SetAudioFileAsync(int id, string? audioFile, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync("UPDATE Beatmapsets SET AudioFile = @AudioFile WHERE Id = @Id",
			new { Id = id, AudioFile = audioFile });
		logger.LogDebug("Beatmapset audio file set: Id={Id}", id);
	}

	/// <inheritdoc />
	/// <remarks>
	///     Beatmaps owned by the set cascade through the Beatmaps-Beatmapsets foreign key, so no
	///     separate cleanup is issued.
	/// </remarks>
	public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync("DELETE FROM Beatmapsets WHERE Id = @Id", new { Id = id });
		logger.LogDebug("Beatmapset deleted: Id={Id}", id);
	}

	/// <inheritdoc />
	public async Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		return await connection.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(Id), 0) FROM Beatmapsets");
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<int>> FetchAllIdsAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var ids = await connection.QueryAsync<int>("SELECT Id FROM Beatmapsets");
		return [.. ids];
	}

	/// <inheritdoc />
	/// <remarks>
	///     When <paramref name="onlyWithVisibleBeatmaps" /> is <see langword="true" /> the query adds
	///     <c>m.IsPrivate = 0</c>; rows are otherwise read back as-is, ordered by id descending.
	/// </remarks>
	public async Task<IReadOnlyList<Beatmapset>> FetchPageAsync(int offset, int limit, bool onlyWithVisibleBeatmaps,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var whereClause = onlyWithVisibleBeatmaps ? "WHERE m.IsPrivate = 0" : "";
		var rows = await connection.QueryAsync<BeatmapsetRow>(
			$"""
			 SELECT m.* FROM Beatmapsets m
			 {whereClause}
			 ORDER BY m.Id DESC LIMIT @Limit OFFSET @Offset
			 """,
			new { Limit = limit, Offset = offset });
		return [.. rows.Select(r => r.ToBeatmapset())];
	}

	/// <inheritdoc />
	/// <remarks>
	///     The count is read from the Counters table under the <c>Beatmapsets:Total</c> or
	///     <c>Beatmapsets:Public</c> counter depending on <paramref name="includePrivate" />, not counted
	///     from the Beatmapsets table directly.
	/// </remarks>
	public async Task<int> FetchCountAsync(bool includePrivate, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		return await connection.ExecuteScalarAsync<int>(
			"SELECT Value FROM Counters WHERE Name = @Name",
			new { Name = includePrivate ? "Beatmapsets:Total" : "Beatmapsets:Public" });
	}

	/// <summary>Creates a new SQLite connection using the repository's connection string.</summary>
	private SqliteConnection Connect()
	{
		return SqliteConnectionFactory.Open(connectionString);
	}

	/// <summary>
	///     A mutable row DTO matching the Beatmapsets table columns.
	/// </summary>
	private sealed class BeatmapsetRow
	{
		public int Id { get; set; }
		public string Artist { get; set; } = "";
		public string Title { get; set; } = "";
		public string Creator { get; set; } = "";
		public DateTime LastUpdate { get; set; }
		public DateTime CreatedAt { get; set; }
		public bool IsFrozen { get; set; }
		public bool IsPrivate { get; set; }
		public string? BackgroundFile { get; set; }
		public string? AudioFile { get; set; }

		/// <summary>Builds a <see cref="Beatmapset" /> from this row.</summary>
		/// <returns>The domain beatmapset.</returns>
		public Beatmapset ToBeatmapset()
		{
			return new Beatmapset(Id, Artist, Title, Creator, LastUpdate, CreatedAt, IsFrozen, IsPrivate,
				BackgroundFile, AudioFile);
		}
	}
}