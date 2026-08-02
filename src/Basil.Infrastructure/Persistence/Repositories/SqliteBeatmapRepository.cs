using System.Text.Json;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IBeatmapRepository" />
/// <remarks>
///     Reads join the Beatmaps and Mapsets tables and map through the private mutable row DTOs
///     (<c>BeatmapRow</c> and <c>MapsetRow</c>), since Dapper materializes by property name rather
///     than through a positional record constructor. Each method opens its own connection.
/// </remarks>
public sealed class SqliteBeatmapRepository(string connectionString, ILogger<SqliteBeatmapRepository> logger)
	: IBeatmapRepository
{
	private const string SharedColumns = """
	                                     b.Md5, b.Id, b.Version, b.Filename, b.TotalLength,
	                                     b.Mode, b.Bpm, b.Cs, b.Ar, b.Od, b.Hp, b.Sr, b.BackgroundFile, b.AudioFile,
	                                     b.PreviewTime, b.ObjectCounts,
	                                     m.Id, m.Artist, m.Title, m.Creator, m.LastUpdate, m.CreatedAt, m.IsFrozen, m.IsPrivate
	                                     """;

	/// <inheritdoc />
	/// <remarks>
	///     Dapper has no multi-map <c>QueryFirstOrDefaultAsync</c> overload, so the JOIN is queried
	///     with <c>QueryAsync</c> and the first row taken. Id, md5, and filename each match at most
	///     one row because of their unique constraints, but setId can match several difficulties
	///     within the same set, in which case any one of them satisfies the lookup. When
	///     <paramref name="includePrivate" /> is <see langword="false" />, a beatmapset-level privacy
	///     filter (<c>m.IsPrivate = 0</c>) is added to the query.
	/// </remarks>
	public async Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
		int? setId = null, bool includePrivate = false, CancellationToken cancellationToken = default)
	{
		if (id is null && md5 is null && filename is null && setId is null)
			throw new ArgumentException("Must provide at least one of id/md5/filename/setId.");

		var conditions = new List<string>();
		var parameters = new DynamicParameters();

		if (id is not null)
		{
			conditions.Add("b.Id = @Id");
			parameters.Add("Id", id);
		}

		if (md5 is not null)
		{
			conditions.Add("b.Md5 = @Md5");
			parameters.Add("Md5", md5);
		}

		if (filename is not null)
		{
			conditions.Add("b.Filename = @Filename");
			parameters.Add("Filename", filename);
		}

		if (setId is not null)
		{
			conditions.Add("b.MapsetId = @MapsetId");
			parameters.Add("MapsetId", setId);
		}

		if (!includePrivate) conditions.Add("m.IsPrivate = 0");

		await using var connection = Connect();
		var beatmaps = await connection.QueryAsync<BeatmapRow, MapsetRow, Beatmap>(
			$"""
			 SELECT {SharedColumns} FROM Beatmaps b JOIN Mapsets m ON b.MapsetId = m.Id
			 WHERE {string.Join(" AND ", conditions)}
			 """,
			(b, m) => b.ToBeatmap(m.ToMapset()),
			parameters,
			splitOn: "Id");
		return beatmaps.FirstOrDefault();
	}

	/// <inheritdoc />
	/// <remarks>
	///     The row is matched by <see cref="Beatmap.Md5" /> first: when that md5 already exists its
	///     id is kept regardless of the incoming id, so a re-ingested difficulty never changes
	///     identity. Otherwise the incoming id is used when positive, or a fresh local id is
	///     allocated from <c>Math.Max(Beatmap.LocalIdFloor, FetchMaxIdAsync() + 1)</c>. The write is
	///     a <c>REPLACE INTO</c> that overwrites every column, and
	///     <see cref="BeatmapObjectCounts" /> is serialized to JSON before storage.
	/// </remarks>
	public async Task<Beatmap> UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
	{
		var existing =
			await FetchOneAsync(md5: beatmap.Md5, includePrivate: true, cancellationToken: cancellationToken);
		int resolvedId;
		if (existing is not null) resolvedId = existing.Id;
		else if (beatmap.Id > 0) resolvedId = beatmap.Id;
		else resolvedId = Math.Max(Beatmap.LocalIdFloor, await FetchMaxIdAsync(cancellationToken) + 1);

		var resolved = beatmap with { Id = resolvedId };

		await using var connection = Connect();
		await connection.ExecuteAsync(
			"""
			REPLACE INTO Beatmaps (
			    Md5, Id, MapsetId, Version, Filename, TotalLength,
			    Mode, Bpm, Cs, Od, Ar, Hp, Sr, BackgroundFile, AudioFile, PreviewTime, ObjectCounts
			) VALUES (
			    @Md5, @Id, @MapsetId, @Version, @Filename, @TotalLength,
			    @Mode, @Bpm, @Cs, @Od, @Ar, @Hp, @Sr, @BackgroundFile, @AudioFile, @PreviewTime, @ObjectCounts
			)
			""",
			new
			{
				resolved.Md5,
				resolved.Id,
				MapsetId = resolved.Beatmapset.Id,
				resolved.Version,
				resolved.Filename,
				TotalLength = (int)resolved.Difficulty.TotalLength.TotalSeconds,
				Mode = (int)resolved.Difficulty.Mode,
				resolved.Difficulty.Bpm,
				resolved.Difficulty.Cs,
				resolved.Difficulty.Od,
				resolved.Difficulty.Ar,
				resolved.Difficulty.Hp,
				resolved.Difficulty.Sr,
				resolved.BackgroundFile,
				resolved.AudioFile,
				resolved.PreviewTime,
				ObjectCounts = JsonSerializer.Serialize(resolved.ObjectCounts)
			});
		logger.LogDebug("Beatmap upserted: Id={Id} Md5={Md5}", resolved.Id, resolved.Md5);

		return resolved;
	}

	/// <inheritdoc />
	public async Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync("DELETE FROM Beatmaps WHERE Md5 = @Md5", new { Md5 = md5 });
		logger.LogDebug("Beatmap deleted: Md5={Md5}", md5);
	}

	/// <inheritdoc />
	/// <remarks>
	///     Runs in two passes: first the distinct matching set ids are collected, newest setId
	///     first, with the page applied at the set level; then the full rows for those sets are
	///     read back and grouped by set. Each set's difficulties come back ordered by star rating
	///     ascending, and only sets whose id survived the first pass are included.
	/// </remarks>
	public async Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(
		string? query, GameMode? mode, int offset, int amount,
		CancellationToken cancellationToken = default)
	{
		var conditions = new List<string> { "m.IsPrivate = 0" };
		var parameters = new DynamicParameters();

		if (query is not null)
		{
			conditions.Add("(m.Artist LIKE @Query OR m.Title LIKE @Query OR m.Creator LIKE @Query)");
			parameters.Add("Query", $"%{query}%");
		}

		if (mode is not null)
		{
			conditions.Add("b.Mode = @Mode");
			parameters.Add("Mode", (int)mode);
		}

		var whereClause = $"WHERE {string.Join(" AND ", conditions)}";
		parameters.Add("Offset", offset);
		parameters.Add("Amount", amount);

		await using var connection = Connect();
		var setIds = (await connection.QueryAsync<int>(
			$"""
			 SELECT DISTINCT b.MapsetId FROM Beatmaps b JOIN Mapsets m ON b.MapsetId = m.Id
			 {whereClause}
			 ORDER BY b.MapsetId DESC LIMIT @Amount OFFSET @Offset
			 """,
			parameters)).ToList();

		if (setIds.Count == 0) return [];

		var rows = await connection.QueryAsync<BeatmapRow, MapsetRow, Beatmap>(
			$"""
			 SELECT {SharedColumns} FROM Beatmaps b JOIN Mapsets m ON b.MapsetId = m.Id
			 WHERE b.MapsetId IN @SetIds AND m.IsPrivate = 0
			 ORDER BY b.Sr ASC
			 """,
			(b, m) => b.ToBeatmap(m.ToMapset()),
			new { SetIds = setIds },
			splitOn: "Id");

		var mapsBySet = rows.GroupBy(b => b.Beatmapset.Id)
			.ToDictionary(g => g.Key, g => (IReadOnlyList<Beatmap>)[.. g]);

		return [.. setIds.Where(mapsBySet.ContainsKey).Select(id => mapsBySet[id])];
	}

	/// <inheritdoc />
	public async Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		return await connection.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(Id), 0) FROM Beatmaps");
	}

	/// <inheritdoc />
	public async Task UpdateDiffAsync(int id, double diff, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync("UPDATE Beatmaps SET Sr = @Sr WHERE Id = @Id", new { Id = id, Sr = diff });
		logger.LogDebug("Beatmap diff updated: Id={Id} Sr={Sr}", id, diff);
	}

	/// <inheritdoc />
	/// <remarks>
	///     Applies the same beatmapset-level privacy filter as <see cref="FetchOneAsync" />, excluding
	///     the set's beatmaps when <paramref name="includePrivate" /> is <see langword="false" />.
	/// </remarks>
	public async Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId, bool includePrivate = false,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var whereClause = includePrivate
			? "WHERE b.MapsetId = @MapsetId"
			: "WHERE b.MapsetId = @MapsetId AND m.IsPrivate = 0";
		var rows = await connection.QueryAsync<BeatmapRow, MapsetRow, Beatmap>(
			$"""
			 SELECT {SharedColumns} FROM Beatmaps b JOIN Mapsets m ON b.MapsetId = m.Id
			 {whereClause}
			 """,
			(b, m) => b.ToBeatmap(m.ToMapset()),
			new { MapsetId = setId },
			splitOn: "Id");
		return [.. rows];
	}

	/// <summary>Creates a new SQLite connection using the repository's connection string.</summary>
	private SqliteConnection Connect()
	{
		return new SqliteConnection(connectionString);
	}

	/// <summary>
	///     A mutable row DTO matching the Beatmaps columns of the shared SELECT, split off from the
	///     Mapsets columns so Dapper's multi-mapping can map each half of the JOIN. Mutable because
	///     Dapper fills by property name, not through a positional record constructor.
	/// </summary>
	private sealed class BeatmapRow
	{
		public string Md5 { get; set; } = "";
		public int Id { get; set; }
		public string Version { get; set; } = "";
		public string Filename { get; set; } = "";
		public int TotalLength { get; set; }
		public int Mode { get; set; }
		public double Bpm { get; set; }
		public double Cs { get; set; }
		public double Ar { get; set; }
		public double Od { get; set; }
		public double Hp { get; set; }
		public double Sr { get; set; }
		public string? BackgroundFile { get; set; }
		public string? AudioFile { get; set; }
		public int? PreviewTime { get; set; }
		public string ObjectCounts { get; set; } = "{}";

		/// <summary>Builds a <see cref="Beatmap" /> from this row, deserializing the JSON object counts.</summary>
		/// <param name="beatmapset">The owning beatmapset, built from the Mapsets half of the JOIN.</param>
		/// <returns>The domain beatmap.</returns>
		/// <exception cref="InvalidOperationException">
		///     The stored <c>ObjectCounts</c> column is not a valid JSON
		///     object-counts payload.
		/// </exception>
		public Beatmap ToBeatmap(Beatmapset beatmapset)
		{
			var objectCounts = JsonSerializer.Deserialize<BeatmapObjectCounts>(ObjectCounts)
			                   ?? throw new InvalidOperationException(
				                   $"Beatmap {Id}'s ObjectCounts column is not a valid ObjectCounts payload.");
			return new Beatmap(
				Md5, Id, beatmapset, Version, Filename,
				new Difficulty((GameMode)Mode, Bpm, TimeSpan.FromSeconds(TotalLength), Cs, Ar, Od, Hp, Sr),
				objectCounts, BackgroundFile, AudioFile, PreviewTime);
		}
	}

	/// <summary>
	///     A mutable row DTO matching the Mapsets columns of the shared SELECT, the other half of
	///     the JOIN's multi-mapping.
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

		/// <summary>Builds a <see cref="Beatmapset" /> from this row.</summary>
		/// <returns>The domain beatmapset.</returns>
		public Beatmapset ToMapset()
		{
			return new Beatmapset(Id, Artist, Title, Creator, LastUpdate, CreatedAt, IsFrozen, IsPrivate);
		}
	}
}