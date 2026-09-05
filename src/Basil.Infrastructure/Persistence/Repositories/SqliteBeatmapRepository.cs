using System.Text.Json;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IBeatmapRepository" />
/// <remarks>
///     Reads join the Beatmaps and Beatmapsets tables. Each method opens its own connection.
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
	///     Id, md5, and filename each match at most one row; setId can match several difficulties
	///     within the same set, in which case any one of them satisfies the lookup. When
	///     <paramref name="includePrivate" /> is <see langword="false" />, a beatmapset-level privacy
	///     filter is applied to the lookup.
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
			conditions.Add("b.BeatmapsetId = @BeatmapsetId");
			parameters.Add("BeatmapsetId", setId);
		}

		if (!includePrivate) conditions.Add("m.IsPrivate = 0");

		await using var connection = Connect();
		var beatmaps = await connection.QueryAsync<BeatmapRow, BeatmapsetRow, Beatmap>(
			$"""
			 SELECT {SharedColumns} FROM Beatmaps b JOIN Beatmapsets m ON b.BeatmapsetId = m.Id
			 WHERE {string.Join(" AND ", conditions)}
			 """,
			(b, m) => b.ToBeatmap(m.ToBeatmapset()),
			parameters,
			splitOn: "Id");
		return beatmaps.FirstOrDefault();
	}

	/// <inheritdoc />
	/// <remarks>
	///     The row is matched by <see cref="Beatmap.Md5" /> first: when that md5 already exists, its
	///     id is kept regardless of the incoming id, so a re-ingested difficulty never changes
	///     identity. Otherwise, the incoming id is used when positive, or a fresh local id is
	///     allocated from <c>Math.Max(Beatmap.LocalIdFloor, FetchMaxIdAsync() + 1)</c>. The writer is
	///     a <c>REPLACE INTO</c> that overwrites every column.
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
			    Md5, Id, BeatmapsetId, Version, Filename, TotalLength,
			    Mode, Bpm, Cs, Od, Ar, Hp, Sr, BackgroundFile, AudioFile, PreviewTime, ObjectCounts
			) VALUES (
			    @Md5, @Id, @BeatmapsetId, @Version, @Filename, @TotalLength,
			    @Mode, @Bpm, @Cs, @Od, @Ar, @Hp, @Sr, @BackgroundFile, @AudioFile, @PreviewTime, @ObjectCounts
			)
			""",
			new
			{
				resolved.Md5,
				resolved.Id,
				BeatmapsetId = resolved.Beatmapset.Id,
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
	///     read back and grouped by set. Each set's difficulties come back ordered by ascending
	///     star rating, and only sets whose id survived the first pass are included.
	/// </remarks>
	public async Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(
		BeatmapsetSearchFilters filters, GameMode? mode, int offset, int amount,
		CancellationToken cancellationToken = default)
	{
		var whereClause = BuildSearchWhereClause(filters, mode, out var parameters);
		parameters.Add("Offset", offset);
		parameters.Add("Amount", amount);

		await using var connection = Connect();
		var setIds = (await connection.QueryAsync<int>(
			$"""
			 SELECT DISTINCT b.BeatmapsetId FROM Beatmaps b JOIN Beatmapsets m ON b.BeatmapsetId = m.Id
			 {whereClause}
			 ORDER BY b.BeatmapsetId DESC LIMIT @Amount OFFSET @Offset
			 """,
			parameters)).ToList();

		if (setIds.Count == 0) return [];

		var rows = await connection.QueryAsync<BeatmapRow, BeatmapsetRow, Beatmap>(
			$"""
			 SELECT {SharedColumns} FROM Beatmaps b JOIN Beatmapsets m ON b.BeatmapsetId = m.Id
			 WHERE b.BeatmapsetId IN @SetIds AND m.IsPrivate = 0
			 ORDER BY b.Sr ASC
			 """,
			(b, m) => b.ToBeatmap(m.ToBeatmapset()),
			new { SetIds = setIds },
			splitOn: "Id");

		var mapsBySet = rows.GroupBy(b => b.Beatmapset.Id)
			.ToDictionary(g => g.Key, g => (IReadOnlyList<Beatmap>)[.. g]);

		return [.. setIds.Where(mapsBySet.ContainsKey).Select(id => mapsBySet[id])];
	}

	/// <inheritdoc />
	public async Task<int> SearchCountAsync(BeatmapsetSearchFilters filters, GameMode? mode,
		CancellationToken cancellationToken = default)
	{
		var whereClause = BuildSearchWhereClause(filters, mode, out var parameters);

		await using var connection = Connect();
		return await connection.ExecuteScalarAsync<int>(
			$"""
			 SELECT COUNT(DISTINCT b.BeatmapsetId) FROM Beatmaps b JOIN Beatmapsets m ON b.BeatmapsetId = m.Id
			 {whereClause}
			 """,
			parameters);
	}

	/// <summary>
	///     Builds the shared `WHERE` clause and parameters for a beatmapset search, from the same
	///     filters <see cref="SearchAsync" /> and <see cref="SearchCountAsync" /> both translate.
	/// </summary>
	private static string BuildSearchWhereClause(BeatmapsetSearchFilters filters, GameMode? mode,
		out DynamicParameters parameters)
	{
		var conditions = new List<string> { "m.IsPrivate = 0" };
		parameters = new DynamicParameters();
		var p = 0;

		if (filters.Keywords is not null)
		{
			conditions.Add("(m.Artist LIKE @Query OR m.Title LIKE @Query OR m.Creator LIKE @Query)");
			parameters.Add("Query", $"%{filters.Keywords}%");
		}

		if (mode is not null)
		{
			conditions.Add("b.Mode = @Mode");
			parameters.Add("Mode", (int)mode);
		}

		AppendNumeric(conditions, parameters, ref p, "b.Sr", filters.Stars);
		AppendNumeric(conditions, parameters, ref p, "b.Ar", filters.Ar);
		AppendNumeric(conditions, parameters, ref p, "b.Hp", filters.Hp);
		AppendNumeric(conditions, parameters, ref p, "b.Cs", filters.Cs);
		AppendNumeric(conditions, parameters, ref p, "b.Od", filters.Od);
		AppendNumeric(conditions, parameters, ref p, "b.Bpm", filters.Bpm);
		AppendNumeric(conditions, parameters, ref p, "b.TotalLength", filters.LengthSeconds);
		// osu!mania's key count and every other mode's circle size share the same stored field,
		// matching real osu!'s own convention -- see BeatmapsetSearchFilters.Keys's own remarks.
		AppendNumeric(conditions, parameters, ref p, "b.Cs", filters.Keys);
		// Circles/sliders only exist on standard-mode beatmaps (BeatmapObjectCounts's polymorphic
		// JSON shape); json_extract returns NULL for every other mode, which naturally excludes them
		// from these comparisons rather than requiring a separate mode check.
		AppendNumeric(conditions, parameters, ref p, "json_extract(b.ObjectCounts, '$.Circles')", filters.Circles);
		AppendNumeric(conditions, parameters, ref p, "json_extract(b.ObjectCounts, '$.Sliders')", filters.Sliders);
		AppendDate(conditions, parameters, ref p, "m.CreatedAt", filters.Created);
		AppendDate(conditions, parameters, ref p, "m.LastUpdate", filters.Updated);

		if (filters.Creator is not null)
		{
			conditions.Add("m.Creator = @Creator COLLATE NOCASE");
			parameters.Add("Creator", filters.Creator);
		}

		if (filters.Artist is not null)
		{
			conditions.Add("m.Artist LIKE @ArtistFilter");
			parameters.Add("ArtistFilter", $"%{filters.Artist}%");
		}

		if (filters.Title is not null)
		{
			conditions.Add("m.Title LIKE @TitleFilter");
			parameters.Add("TitleFilter", $"%{filters.Title}%");
		}

		if (filters.Difficulty is not null)
		{
			conditions.Add("b.Version LIKE @DifficultyFilter");
			parameters.Add("DifficultyFilter", $"%{filters.Difficulty}%");
		}

		if (filters.Status is not null)
		{
			// Every set on this server reports the same status (Beatmapset.Status is a constant), so
			// this either matches everything or nothing depending on which status was asked for.
			conditions.Add(filters.Status == Beatmapset.Status ? "1 = 1" : "1 = 0");
		}

		return $"WHERE {string.Join(" AND ", conditions)}";
	}

	/// <summary>Appends a `column &lt;op&gt; @paramName` condition for a numeric search filter, if one was given.</summary>
	private static void AppendNumeric<T>(List<string> conditions, DynamicParameters parameters, ref int paramIndex,
		string column, ComparableFilter<T>? filter) where T : struct
	{
		if (filter is null) return;
		var name = $"F{paramIndex++}";
		conditions.Add($"{column} {ToSql(filter.Operator)} @{name}");
		parameters.Add(name, filter.Value);
	}

	/// <summary>
	///     Appends a condition for a date search filter, if one was given -- see
	///     <see cref="DateFilter" />'s own remarks for how each operator maps to
	///     <see cref="DateFilter.RangeStart" />/<see cref="DateFilter.RangeEnd" />.
	/// </summary>
	private static void AppendDate(List<string> conditions, DynamicParameters parameters, ref int paramIndex,
		string column, DateFilter? filter)
	{
		if (filter is null) return;
		var name = $"F{paramIndex++}";

		switch (filter.Operator)
		{
			case ComparisonOperator.Equal:
				conditions.Add($"{column} >= @{name}Start AND {column} < @{name}End");
				parameters.Add($"{name}Start", filter.RangeStart.UtcDateTime);
				parameters.Add($"{name}End", filter.RangeEnd.UtcDateTime);
				break;
			case ComparisonOperator.GreaterThan:
				conditions.Add($"{column} >= @{name}");
				parameters.Add(name, filter.RangeEnd.UtcDateTime);
				break;
			case ComparisonOperator.LessThanOrEqual:
				conditions.Add($"{column} < @{name}");
				parameters.Add(name, filter.RangeEnd.UtcDateTime);
				break;
			case ComparisonOperator.GreaterThanOrEqual:
				conditions.Add($"{column} >= @{name}");
				parameters.Add(name, filter.RangeStart.UtcDateTime);
				break;
			case ComparisonOperator.LessThan:
				conditions.Add($"{column} < @{name}");
				parameters.Add(name, filter.RangeStart.UtcDateTime);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(filter));
		}
	}

	private static string ToSql(ComparisonOperator op)
	{
		return op switch
		{
			ComparisonOperator.Equal => "=",
			ComparisonOperator.LessThan => "<",
			ComparisonOperator.LessThanOrEqual => "<=",
			ComparisonOperator.GreaterThan => ">",
			ComparisonOperator.GreaterThanOrEqual => ">=",
			_ => throw new ArgumentOutOfRangeException(nameof(op))
		};
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
			? "WHERE b.BeatmapsetId = @BeatmapsetId"
			: "WHERE b.BeatmapsetId = @BeatmapsetId AND m.IsPrivate = 0";
		var rows = await connection.QueryAsync<BeatmapRow, BeatmapsetRow, Beatmap>(
			$"""
			 SELECT {SharedColumns} FROM Beatmaps b JOIN Beatmapsets m ON b.BeatmapsetId = m.Id
			 {whereClause}
			 """,
			(b, m) => b.ToBeatmap(m.ToBeatmapset()),
			new { BeatmapsetId = setId },
			splitOn: "Id");
		return [.. rows];
	}

	/// <inheritdoc />
	/// <remarks>Applies the same beatmapset-level privacy filter as <see cref="FetchOneAsync" />.</remarks>
	public async Task<IReadOnlyDictionary<int, int>> FetchCountsBySetIdsAsync(IReadOnlyCollection<int> setIds,
		bool includePrivate = false, CancellationToken cancellationToken = default)
	{
		if (setIds.Count == 0) return new Dictionary<int, int>();

		await using var connection = Connect();
		var whereClause = includePrivate
			? "WHERE b.BeatmapsetId IN @SetIds"
			: "WHERE b.BeatmapsetId IN @SetIds AND m.IsPrivate = 0";
		var rows = await connection.QueryAsync<SetBeatmapCount>(
			$"""
			 SELECT b.BeatmapsetId AS SetId, COUNT(*) AS Count FROM Beatmaps b JOIN Beatmapsets m ON b.BeatmapsetId = m.Id
			 {whereClause}
			 GROUP BY b.BeatmapsetId
			 """,
			new { SetIds = setIds });
		return rows.ToDictionary(r => r.SetId, r => r.Count);
	}

	/// <summary>Creates a new SQLite connection using the repository's connection string.</summary>
	private SqliteConnection Connect()
	{
		return SqliteConnectionFactory.Open(connectionString);
	}

	/// <summary>A row DTO for the grouped per-set beatmap count.</summary>
	private sealed class SetBeatmapCount
	{
		public int SetId { get; set; }
		public int Count { get; set; }
	}

	/// <summary>
	///     A row DTO matching the Beatmaps columns of the shared SELECT, split off from the
	///     Beatmapsets columns so each half of the JOIN maps separately.
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
		/// <param name="beatmapset">The owning beatmapset, built from the Beatmapsets half of the JOIN.</param>
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
	///     A mutable row DTO matching the Beatmapsets columns of the shared SELECT, the other half
	///     of the JOIN.
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

		/// <summary>Builds a <see cref="Beatmapset" /> from this row.</summary>
		/// <returns>The domain beatmapset.</returns>
		public Beatmapset ToBeatmapset()
		{
			return new Beatmapset(Id, Artist, Title, Creator, LastUpdate, CreatedAt, IsFrozen, IsPrivate);
		}
	}
}