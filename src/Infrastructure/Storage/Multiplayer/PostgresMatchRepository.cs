using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Storage.Common.Database;
using Basil.Infrastructure.Storage.Common.Writing;
using Basil.Infrastructure.Storage.Memory;
using Dapper;

namespace Basil.Infrastructure.Storage.Multiplayer;

/// <summary>Stores matches.</summary>
internal sealed class PostgresMatchRepository(DatabaseReader reader, DatabaseWriter writer, IUserRepository users)
	: MemoryRepository<int, Match>(reader, writer), IMatchRepository
{
	protected override string WriteSql =>
		"""
		insert into matches (id, name, creator_id, started_at, ended_at, is_private)
		values (@Id, @Name, @CreatorId, @StartedAt, @EndedAt, @IsPrivate)
		on conflict (id) do update set
			name = excluded.name,
			creator_id = excluded.creator_id,
			started_at = excluded.started_at,
			ended_at = excluded.ended_at,
			is_private = excluded.is_private;
		""";

	/// <inheritdoc />
	public async Task<Match> CreateAsync(MatchData data, CancellationToken cancellationToken = default)
	{
		var value = await WithCreatorAsync(data, cancellationToken);
		var parameters = Parameters(value);
		var id = await Writer.WriteAsync(Root.Server, (connection, transaction) => connection.QuerySingleAsync<int>(
			"""
			insert into matches (name, creator_id, started_at, ended_at, is_private)
			values (@Name, @CreatorId, @StartedAt, @EndedAt, @IsPrivate)
			returning id;
			""",
			parameters, transaction), cancellationToken);

		return Track(new Match { Id = id, Value = value });
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(Match match, CancellationToken cancellationToken = default)
	{
		_ = SaveAsync(match);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public ValueTask<Match?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		return FindAsync(id, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Page<Match>> ListAsync(MatchQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var where = BuildFilter(query);
		var (total, rows) = await Reader.ReadAsync(async connection => (
			await connection.ExecuteScalarAsync<int>($"select count(*) from matches {where}"),
			await connection.QueryAsync<MatchRow>(
				$"""
				  select id, name, creator_id, started_at, ended_at, is_private from matches
				  {where}
				  order by started_at desc, id desc
				  limit @Limit offset @Offset
				 """,
				new { page.Limit, page.Offset })), cancellationToken);

		var matches = new List<Match>();
		foreach (var row in rows)
			matches.Add(Track(await ToMatchAsync(row, cancellationToken)));

		return new Page<Match>(matches, total);
	}

	protected override int KeyOf(Match item)
	{
		return item.Id;
	}

	protected override Root RootOf(Match item)
	{
		return Root.Match(item.Id);
	}

	protected override void CopyTo(Match live, Match from)
	{
		live.Value.Name = from.Value.Name;
		live.Value.EndedAt = from.Value.EndedAt;
		live.Value.IsPrivate = from.Value.IsPrivate;
	}

	protected override async Task<Match?> LoadAsync(int key)
	{
		var row = await Reader.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<MatchRow>(
			"select id, name, creator_id, started_at, ended_at, is_private from matches where id = @Id",
			new { Id = key }));
		return row is null ? null : await ToMatchAsync(row, default);
	}

	protected override object WriteParameters(Match match)
	{
		var value = match.Value;
		return new
		{
			match.Id,
			value.Name,
			CreatorId = value.Creator?.Id,
			StartedAt = value.StartedAt.ToUniversalTime(),
			EndedAt = value.EndedAt?.ToUniversalTime(),
			value.IsPrivate
		};
	}

	private static string BuildFilter(MatchQuery query)
	{
		var conditions = new List<string>();
		if (query.Ended is true) conditions.Add("ended_at is not null");
		else if (query.Ended is false) conditions.Add("ended_at is null");
		if (!query.IncludePrivate) conditions.Add("not is_private");
		return conditions.Count == 0 ? "" : $"where {string.Join(" AND ", conditions)}";
	}

	private async Task<Match> ToMatchAsync(MatchRow row, CancellationToken cancellationToken)
	{
		var creator = row.CreatorId is { } creatorId
			? await users.GetAsync(creatorId, cancellationToken)
			: null;
		return new Match
		{
			Id = row.Id,
			Value = new MatchData
			{
				Name = row.Name,
				Creator = creator,
				StartedAt = row.StartedAt,
				EndedAt = row.EndedAt,
				IsPrivate = row.IsPrivate
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
			StartedAt = data.StartedAt.ToUniversalTime(),
			EndedAt = data.EndedAt?.ToUniversalTime(),
			data.IsPrivate
		};
	}

	/// <summary>A stored row of the <c>matches</c> table.</summary>
	private sealed class MatchRow
	{
		public int Id { get; set; }
		public string Name { get; set; } = "";
		public int? CreatorId { get; set; }
		public DateTimeOffset StartedAt { get; set; }
		public DateTimeOffset? EndedAt { get; set; }
		public bool IsPrivate { get; set; }
	}
}