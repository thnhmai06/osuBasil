using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Storage.Batching;
using Basil.Infrastructure.Storage.Caching;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage.Multiplayer;

/// <summary>Stores matches.</summary>
internal sealed class SqliteMatchRepository(DatabaseBatcher batcher, IUserRepository users)
	: CachedRepository<int, Match>(batcher), IMatchRepository
{
	protected override int KeyOf(Match item) => item.Id;

	/// <inheritdoc />
	public async Task<Match> CreateAsync(MatchData data, CancellationToken cancellationToken = default)
	{
		var value = await WithCreatorAsync(data, cancellationToken);
		var parameters = Parameters(value);
		var id = await Batcher.WriteAsync((connection, transaction) => connection.QuerySingleAsync<int>(
			"""
			INSERT INTO Matches (Name, CreatorId, StartedAt, EndedAt, IsPrivate)
			VALUES (@Name, @CreatorId, @StartedAt, @EndedAt, @IsPrivate)
			RETURNING Id
			""",
			parameters, transaction), cancellationToken);

		return Track(new Match { Id = id, Value = value });
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(Match match, CancellationToken cancellationToken = default)
	{
		var live = Track(match);
		if (!ReferenceEquals(live, match))
		{
			live.Value.Name = match.Value.Name;
			live.Value.EndedAt = match.Value.EndedAt;
			live.Value.IsPrivate = match.Value.IsPrivate;
		}

		return SaveAsync(live);
	}

	/// <inheritdoc />
	public ValueTask<Match?> GetAsync(int id, CancellationToken cancellationToken = default) => FindAsync(id, cancellationToken);

	/// <inheritdoc />
	public async Task<Page<Match>> ListAsync(MatchQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var where = BuildFilter(query);
		var (total, rows) = await Batcher.ReadAsync(async (connection, transaction) => (
			await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM Matches {where}", transaction: transaction),
			await connection.QueryAsync<MatchRow>(
			$"""
			 SELECT Id, Name, CreatorId, StartedAt, EndedAt, IsPrivate FROM Matches
			 {where}
			 ORDER BY StartedAt DESC, Id DESC
			 LIMIT @Limit OFFSET @Offset
			""",
			new { Limit = page.Limit, Offset = page.Offset }, transaction)), cancellationToken);

		var matches = new List<Match>();
		foreach (var row in rows)
			matches.Add(Track(await ToMatchAsync(row, cancellationToken)));

		return new Page<Match>(matches, total);
	}

	protected override async Task<Match?> LoadAsync(int key, CancellationToken cancellationToken)
	{
		var row = await Batcher.ReadAsync((connection, transaction) => connection.QuerySingleOrDefaultAsync<MatchRow>(
			"SELECT Id, Name, CreatorId, StartedAt, EndedAt, IsPrivate FROM Matches WHERE Id = @Id",
			new { Id = key }, transaction), cancellationToken);
		return row is null ? null : await ToMatchAsync(row, cancellationToken);
	}

	protected override string WriteSql =>
		"""
		INSERT INTO Matches (Id, Name, CreatorId, StartedAt, EndedAt, IsPrivate)
		VALUES (@Id, @Name, @CreatorId, @StartedAt, @EndedAt, @IsPrivate)
		ON CONFLICT(Id) DO UPDATE SET
			Name = excluded.Name,
			CreatorId = excluded.CreatorId,
			StartedAt = excluded.StartedAt,
			EndedAt = excluded.EndedAt,
			IsPrivate = excluded.IsPrivate
		""";

	protected override object WriteParameters(Match match)
	{
		var value = match.Value;
		return new
		{
			match.Id,
			value.Name,
			CreatorId = value.Creator?.Id,
			StartedAt = value.StartedAt.ToUnixTimeMilliseconds(),
			EndedAt = value.EndedAt?.ToUnixTimeMilliseconds(),
			IsPrivate = value.IsPrivate ? 1 : 0
		};
	}

	private static string BuildFilter(MatchQuery query)
	{
		var conditions = new List<string>();
		if (query.Ended is true) conditions.Add("EndedAt IS NOT NULL");
		else if (query.Ended is false) conditions.Add("EndedAt IS NULL");
		if (!query.IncludePrivate) conditions.Add("IsPrivate = 0");
		return conditions.Count == 0 ? "" : $"WHERE {string.Join(" AND ", conditions)}";
	}

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

	private async Task<MatchData> WithCreatorAsync(MatchData data, CancellationToken cancellationToken)
	{
		var creator = data.Creator is { } user ? await users.GetAsync(user.Id, cancellationToken) ?? user : null;
		return new MatchData
		{
			Name = data.Name,
			Creator = creator,
			StartedAt = data.StartedAt,
			EndedAt = data.EndedAt,
			IsPrivate = data.IsPrivate
		};
	}

	private static object Parameters(MatchData data)
	{
		return new
		{
			data.Name,
			CreatorId = data.Creator?.Id,
			StartedAt = data.StartedAt.ToUnixTimeMilliseconds(),
			EndedAt = data.EndedAt?.ToUnixTimeMilliseconds(),
			IsPrivate = data.IsPrivate ? 1 : 0
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
