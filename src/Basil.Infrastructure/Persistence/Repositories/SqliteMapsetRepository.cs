using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IMapsetRepository" />
/// <remarks>
///     Rows map through the private mutable <c>MapsetRow</c> DTO, since Dapper fills by property
///     name rather than through a positional record constructor. Each method opens its own
///     connection.
/// </remarks>
public sealed class SqliteMapsetRepository(string connectionString, ILogger<SqliteMapsetRepository> logger)
	: IMapsetRepository
{
	/// <inheritdoc />
	public async Task<Mapset?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var row = await connection.QuerySingleOrDefaultAsync<MapsetRow>(
			"SELECT * FROM Mapsets WHERE Id = @Id", new { Id = id });
		return row?.ToMapset();
	}

	/// <inheritdoc />
	/// <remarks>
	///     Uses <c>INSERT ... ON CONFLICT DO UPDATE</c>, not <c>REPLACE INTO</c>: a replace deletes
	///     then reinserts on a primary-key conflict, and that delete cascades through the
	///     Beatmaps-Mapsets foreign key, wiping every beatmap under the set on every re-upsert. The
	///     update clause overwrites only the shared metadata columns; <see cref="Mapset.IsFrozen" />,
	///     <see cref="Mapset.IsPrivate" />, and the media-file columns are deliberately left alone so
	///     a re-ingestion pass never clears an admin-set freeze lock or privacy flag, and because the
	///     pass's actual background and audio files are not known yet (those are set later via
	///     <see cref="SetBackgroundFileAsync" /> and <see cref="SetAudioFileAsync" />). The persisted
	///     row is re-read and returned.
	/// </remarks>
	public async Task<Mapset> UpsertAsync(Mapset mapset, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync(
			"""
			INSERT INTO Mapsets (Id, Artist, Title, Creator, LastUpdate, CreatedAt, IsFrozen, IsPrivate, BackgroundFile, AudioFile)
			VALUES (@Id, @Artist, @Title, @Creator, @LastUpdate, @CreatedAt, @IsFrozen, @IsPrivate, @BackgroundFile, @AudioFile)
			ON CONFLICT(Id) DO UPDATE SET
			    Artist = excluded.Artist, Title = excluded.Title, Creator = excluded.Creator,
			    LastUpdate = excluded.LastUpdate, CreatedAt = excluded.CreatedAt
			""",
			new
			{
				mapset.Id,
				mapset.Artist,
				mapset.Title,
				mapset.Creator,
				mapset.LastUpdate,
				mapset.CreatedAt,
				mapset.IsFrozen,
				mapset.IsPrivate,
				mapset.BackgroundFile,
				mapset.AudioFile
			});
		logger.LogDebug("Mapset upserted: Id={Id}", mapset.Id);

		return (await FetchByIdAsync(mapset.Id, cancellationToken))!;
	}

	/// <inheritdoc />
	public async Task SetFrozenAsync(int id, bool frozen, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync("UPDATE Mapsets SET IsFrozen = @Frozen WHERE Id = @Id",
			new { Id = id, Frozen = frozen });
		logger.LogDebug("Mapset frozen flag set: Id={Id} Frozen={Frozen}", id, frozen);
	}

	/// <inheritdoc />
	public async Task SetPrivateAsync(int id, bool isPrivate, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync("UPDATE Mapsets SET IsPrivate = @IsPrivate WHERE Id = @Id",
			new { Id = id, IsPrivate = isPrivate });
		logger.LogDebug("Mapset private flag set: Id={Id} IsPrivate={IsPrivate}", id, isPrivate);
	}

	/// <inheritdoc />
	public async Task SetBackgroundFileAsync(int id, string? backgroundFile,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync("UPDATE Mapsets SET BackgroundFile = @BackgroundFile WHERE Id = @Id",
			new { Id = id, BackgroundFile = backgroundFile });
		logger.LogDebug("Mapset background file set: Id={Id}", id);
	}

	/// <inheritdoc />
	public async Task SetAudioFileAsync(int id, string? audioFile, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync("UPDATE Mapsets SET AudioFile = @AudioFile WHERE Id = @Id",
			new { Id = id, AudioFile = audioFile });
		logger.LogDebug("Mapset audio file set: Id={Id}", id);
	}

	/// <inheritdoc />
	/// <remarks>
	///     Beatmaps owned by the set cascade through the Beatmaps-Mapsets foreign key, so no
	///     separate cleanup is issued.
	/// </remarks>
	public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		// Beatmaps rows cascade via Beatmaps_Mapsets_Id_fk (on delete cascade), so no manual cleanup is needed.
		await connection.ExecuteAsync("DELETE FROM Mapsets WHERE Id = @Id", new { Id = id });
		logger.LogDebug("Mapset deleted: Id={Id}", id);
	}

	/// <inheritdoc />
	public async Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		return await connection.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(Id), 0) FROM Mapsets");
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<int>> FetchAllIdsAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var ids = await connection.QueryAsync<int>("SELECT Id FROM Mapsets");
		return [.. ids];
	}

	/// <inheritdoc />
	/// <remarks>
	///     When <paramref name="onlyWithVisibleBeatmaps" /> is <see langword="true" /> the query adds
	///     <c>m.IsPrivate = 0</c>; rows are otherwise read back as-is, ordered by id descending.
	/// </remarks>
	public async Task<IReadOnlyList<Mapset>> FetchPageAsync(int offset, int limit, bool onlyWithVisibleBeatmaps,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var whereClause = onlyWithVisibleBeatmaps ? "WHERE m.IsPrivate = 0" : "";
		var rows = await connection.QueryAsync<MapsetRow>(
			$"""
			 SELECT m.* FROM Mapsets m
			 {whereClause}
			 ORDER BY m.Id DESC LIMIT @Limit OFFSET @Offset
			 """,
			new { Limit = limit, Offset = offset });
		return [.. rows.Select(r => r.ToMapset())];
	}

	/// <inheritdoc />
	/// <remarks>
	///     The count is read from the Counters table under the <c>Mapsets:Total</c> or
	///     <c>Mapsets:Public</c> counter depending on <paramref name="includePrivate" />, not counted
	///     from the Mapsets table directly.
	/// </remarks>
	public async Task<int> FetchCountAsync(bool includePrivate, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		return await connection.ExecuteScalarAsync<int>(
			"SELECT Value FROM Counters WHERE Name = @Name",
			new { Name = includePrivate ? "Mapsets:Total" : "Mapsets:Public" });
	}

	/// <summary>Creates a new SQLite connection using the repository's connection string.</summary>
	private SqliteConnection Connect()
	{
		return new SqliteConnection(connectionString);
	}

	/// <summary>
	///     A mutable row DTO matching the Mapsets table columns. Mutable because Dapper fills by
	///     property name, not through a positional record constructor.
	/// </summary>
	private sealed class MapsetRow
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

		/// <summary>Builds a <see cref="Mapset" /> from this row.</summary>
		/// <returns>The domain mapset.</returns>
		public Mapset ToMapset()
		{
			return new Mapset(Id, Artist, Title, Creator, LastUpdate, CreatedAt, IsFrozen, IsPrivate,
				BackgroundFile, AudioFile);
		}
	}
}