using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Multiplayer;
using Dapper;

namespace Basil.Infrastructure.Storage.Multiplayer;

/// <summary>Stores matches.</summary>
internal sealed class SqliteMatchRepository(Database database, IUserRepository users) : IMatchRepository
{
	/// <inheritdoc />
	public async Task<Match> CreateAsync(MatchData data, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var id = await connection.QuerySingleAsync<int>(
			"""
			INSERT INTO Matches (Name, CreatorId, StartedAt, EndedAt, IsPrivate)
			VALUES (@Name, @CreatorId, @StartedAt, @EndedAt, @IsPrivate)
			RETURNING Id
			""",
			new
			{
				data.Name,
				CreatorId = data.Creator?.Id,
				StartedAt = data.StartedAt.ToUnixTimeMilliseconds(),
				EndedAt = data.EndedAt?.ToUnixTimeMilliseconds(),
				IsPrivate = data.IsPrivate ? 1 : 0
			});

		return new Match { Id = id, Value = data };
	}

	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(Match match, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		await connection.ExecuteAsync(
			"""
			INSERT INTO Matches (Id, Name, CreatorId, StartedAt, EndedAt, IsPrivate)
			VALUES (@Id, @Name, @CreatorId, @StartedAt, @EndedAt, @IsPrivate)
			ON CONFLICT(Id) DO UPDATE SET
				Name = excluded.Name,
				CreatorId = excluded.CreatorId,
				StartedAt = excluded.StartedAt,
				EndedAt = excluded.EndedAt,
				IsPrivate = excluded.IsPrivate
			""",
			new
			{
				match.Id,
				match.Value.Name,
				CreatorId = match.Value.Creator?.Id,
				StartedAt = match.Value.StartedAt.ToUnixTimeMilliseconds(),
				EndedAt = match.Value.EndedAt?.ToUnixTimeMilliseconds(),
				IsPrivate = match.Value.IsPrivate ? 1 : 0
			});
	}

	/// <inheritdoc />
	public async ValueTask<Match?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var row = await connection.QuerySingleOrDefaultAsync<MatchRow>(
			"SELECT Id, Name, CreatorId, StartedAt, EndedAt, IsPrivate FROM Matches WHERE Id = @Id",
			new { Id = id });
		return row is null ? null : await ToMatchAsync(row, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Page<Match>> ListAsync(MatchQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var where = BuildFilter(query);

		await using var connection = await database.OpenAsync(cancellationToken);
		var total = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM Matches {where}");

		var rows = (await connection.QueryAsync<MatchRow>(
			$"""
			 SELECT Id, Name, CreatorId, StartedAt, EndedAt, IsPrivate FROM Matches
			 {where}
			 ORDER BY StartedAt DESC, Id DESC
			 LIMIT @Limit OFFSET @Offset
			 """,
			new { Limit = page.Limit, Offset = page.Offset })).ToList();

		var matches = new List<Match>(rows.Count);
		foreach (var row in rows)
			matches.Add(await ToMatchAsync(row, cancellationToken));

		return new Page<Match>(matches, total);
	}

	/// <summary>Builds the WHERE clause of a match listing.</summary>
	private static string BuildFilter(MatchQuery query)
	{
		var conditions = new List<string>();

		if (query.Ended is true) conditions.Add("EndedAt IS NOT NULL");
		else if (query.Ended is false) conditions.Add("EndedAt IS NULL");

		if (!query.IncludePrivate) conditions.Add("IsPrivate = 0");

		return conditions.Count == 0 ? "" : $"WHERE {string.Join(" AND ", conditions)}";
	}

	/// <summary>Builds a match from its stored row, resolving its creator.</summary>
	private async Task<Match> ToMatchAsync(MatchRow row, CancellationToken cancellationToken)
	{
		var creator = row.CreatorId is { } creatorId
			? await users.GetAsync((int)creatorId, cancellationToken)
			: null;

		return new Match
		{
			Id = row.Id,
			Value = new MatchData
			{
				Name = row.Name,
				Creator = creator,
				StartedAt = DateTimeOffset.FromUnixTimeMilliseconds(row.StartedAt),
				EndedAt = row.EndedAt is { } endedAt ? DateTimeOffset.FromUnixTimeMilliseconds(endedAt) : null,
				IsPrivate = row.IsPrivate != 0
			}
		};
	}

	/// <summary>A stored row of the Matches table.</summary>
	private sealed class MatchRow
	{
		public int Id { get; set; }
		public string Name { get; set; } = "";
		public long? CreatorId { get; set; }
		public long StartedAt { get; set; }
		public long? EndedAt { get; set; }
		public long IsPrivate { get; set; }
	}
}
