using Basil.Application.Persistence.Common;
using Basil.Application.Persistence.Multiplayer;
using Basil.Application.Persistence.Users;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Persistence.Postgres;
using Basil.Infrastructure.Persistence.Memory;
using Dapper;

namespace Basil.Infrastructure.Persistence.Multiplayer;

/// <summary>Stores matches.</summary>
internal sealed class PostgresMatchRepository(DatabaseReader reader, DatabaseWriter writer, IUserRepository users)
	: MemoryRepository<int, MatchRecord>(reader, writer), IMatchRepository
{
	protected override string WriteSql =>
		"""
		update matches set
			name = @Name,
			creator_id = @CreatorId,
			started_at = @StartedAt,
			ended_at = @EndedAt,
			is_private = @IsPrivate
		where id = @Id;
		""";

	/// <inheritdoc />
	public async Task<MatchRecord> CreateAsync(MatchData data, CancellationToken cancellationToken = default)
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

		return Track(new MatchRecord { Id = id, Value = value });
	}

	/// <inheritdoc />
	public Task UpdateAsync(MatchRecord match, CancellationToken cancellationToken = default)
	{
		_ = SaveAsync(match);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public ValueTask<MatchRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		return FindAsync(id, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Page<MatchRecord>> ListAsync(MatchQuery query, PageRequest page,
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

		var matches = new List<MatchRecord>();
		foreach (var row in rows)
			matches.Add(Track(await ToMatchAsync(row, cancellationToken)));

		return new Page<MatchRecord>(matches, total);
	}

	protected override int KeyOf(MatchRecord item)
	{
		return item.Id;
	}

	protected override Root RootOf(MatchRecord item)
	{
		return Root.Match(item.Id);
	}

	protected override void CopyTo(MatchRecord live, MatchRecord from)
	{
		live.Value.Name = from.Value.Name;
		live.Value.EndedAt = from.Value.EndedAt;
		live.Value.IsPrivate = from.Value.IsPrivate;
	}

	protected override async Task<MatchRecord?> LoadAsync(int key)
	{
		var row = await Reader.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<MatchRow>(
			"select id, name, creator_id, started_at, ended_at, is_private from matches where id = @Id",
			new { Id = key }));
		return row is null ? null : await ToMatchAsync(row, CancellationToken.None);
	}

	protected override object WriteParameters(MatchRecord match)
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
		switch (query.Ended)
		{
			case true:
				conditions.Add("ended_at is not null");
				break;
			case false:
				conditions.Add("ended_at is null");
				break;
		}

		if (query.Private is { } isPrivate) conditions.Add(isPrivate ? "is_private" : "not is_private");
		return conditions.Count == 0 ? "" : $"where {string.Join(" AND ", conditions)}";
	}

	private async Task<MatchRecord> ToMatchAsync(MatchRow row, CancellationToken cancellationToken)
	{
		var creator = row.CreatorId is { } creatorId
			? await users.GetAsync(creatorId, cancellationToken)
			: null;
		return new MatchRecord
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