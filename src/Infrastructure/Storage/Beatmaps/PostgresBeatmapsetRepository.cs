using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Storage.Caching;
using Basil.Infrastructure.Storage.Writing;
using Dapper;
using Npgsql;

namespace Basil.Infrastructure.Storage.Beatmaps;

/// <summary>Stores beatmapsets in the <c>beatmapsets</c> table.</summary>
internal sealed class PostgresBeatmapsetRepository(Database database, DatabaseWriter writer)
	: CachedRepository<int, Beatmapset>(database, writer), IBeatmapsetRepository
{
	protected override int KeyOf(Beatmapset item) => item.Id;

	protected override Root RootOf(Beatmapset item) => Root.Beatmapset(item.Id);

	/// <inheritdoc />
	public ValueTask<Beatmapset?> GetAsync(int id, CancellationToken cancellationToken = default) => FindAsync(id, cancellationToken);

	/// <inheritdoc />
	public async Task<Beatmapset> CreateAsync(BeatmapsetData data, int? onlineId = null,
		CancellationToken cancellationToken = default)
	{
		var parameters = new DynamicParameters(new
		{
			id = 0,
			artist = data.Artist,
			title = data.Title,
			creator = data.Creator,
			createdAt = data.CreatedAt.ToUniversalTime(),
			updatedAt = data.UpdatedAt.ToUniversalTime(),
			locked = data.Locked,
			visible = data.Visible
		});
		var id = await Writer.WriteAsync(onlineId is { } given ? Root.Beatmapset(given) : Root.Server,
			async (connection, transaction) =>
		{
			var newId = onlineId ?? await NextLocalIdAsync(connection, transaction);
			parameters.Add("id", newId);
			await connection.ExecuteAsync(
				"""
				insert into beatmapsets (id, artist, title, creator, created_at, updated_at, locked, visible)
				values (@id, @artist, @title, @creator, @createdAt, @updatedAt, @locked, @visible)
				""",
				parameters,
				transaction);
			return newId;
		}, cancellationToken);

		return Track(new Beatmapset { Id = id, Value = data });
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		var live = Track(set);
		if (!ReferenceEquals(live, set))
		{
			live.Value.Artist = set.Value.Artist;
			live.Value.Title = set.Value.Title;
			live.Value.Creator = set.Value.Creator;
			live.Value.UpdatedAt = set.Value.UpdatedAt;
			live.Value.Locked = set.Value.Locked;
			live.Value.Visible = set.Value.Visible;
		}

		_ = SaveAsync(live);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		_ = RemoveAsync(set);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<Page<Beatmapset>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var parameters = new DynamicParameters(new { limit = page.Limit, offset = page.Offset });
		var where = BeatmapQueryFilter.Build(query, parameters);
		var existsFilter = where.Length == 0 ? "" : $" AND {where}";

		var sql = $"""
		           select s.id, s.artist, s.title, s.creator, s.created_at, s.updated_at, s.locked, s.visible
		           from beatmapsets s
		           where exists (
		               select 1 from beatmaps b
		               where b.beatmapset_id = s.id{existsFilter}
		           )
		           order by s.id desc
		           limit @limit offset @offset
		           """;

		var countSql = $"""
		                select count(*)
		                from beatmapsets s
		                where exists (
		                    select 1 from beatmaps b
		                    where b.beatmapset_id = s.id{existsFilter}
		                )
		                """;

		var (rows, total) = await Database.ReadAsync(async connection => (
			await connection.QueryAsync<BeatmapsetRow>(sql, parameters),
			await connection.ExecuteScalarAsync<int>(countSql, parameters)), cancellationToken);

		return new Page<Beatmapset>(rows.Select(row => Track(ToBeatmapset(row))).ToList(), total);
	}

	protected override async Task<Beatmapset?> LoadAsync(int key, CancellationToken cancellationToken)
	{
		var row = await Database.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<BeatmapsetRow>(
			"select id, artist, title, creator, created_at, updated_at, locked, visible from beatmapsets where id = @Id",
			new { Id = key }), cancellationToken);
		return row is null ? null : ToBeatmapset(row);
	}

	protected override string WriteSql =>
		"""
		insert into beatmapsets (id, artist, title, creator, created_at, updated_at, locked, visible)
		values (@Id, @Artist, @Title, @Creator, @CreatedAt, @UpdatedAt, @Locked, @Visible)
		on conflict (id) do update set
			artist = excluded.artist,
			title = excluded.title,
			creator = excluded.creator,
			created_at = excluded.created_at,
			updated_at = excluded.updated_at,
			locked = excluded.locked,
			visible = excluded.visible;
		""";

	protected override object WriteParameters(Beatmapset set)
	{
		var value = set.Value;
		return new
		{
			set.Id,
			value.Artist,
			value.Title,
			value.Creator,
			CreatedAt = value.CreatedAt.ToUniversalTime(),
			UpdatedAt = value.UpdatedAt.ToUniversalTime(),
			Locked = value.Locked,
			Visible = value.Visible
		};
	}

	protected override WriteCommand EraseCommand(int key)
	{
		return new WriteCommand("delete from beatmapsets where id = @Id", new { Id = key });
	}

	private static Task<int> NextLocalIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
	{
		return connection.ExecuteScalarAsync<int>("select nextval('local_beatmapset_ids')::int", transaction: transaction);
	}

	private static Beatmapset ToBeatmapset(BeatmapsetRow row)
	{
		return new Beatmapset
		{
			Id = row.Id,
			Value = new BeatmapsetData
			{
				Artist = row.Artist,
				Title = row.Title,
				Creator = row.Creator,
				CreatedAt = row.CreatedAt,
				UpdatedAt = row.UpdatedAt,
				Locked = row.Locked,
				Visible = row.Visible
			}
		};
	}

	/// <summary>A stored row of the <c>beatmapsets</c> table.</summary>
	private sealed class BeatmapsetRow
	{
		public int Id { get; set; }
		public string Artist { get; set; } = "";
		public string Title { get; set; } = "";
		public string Creator { get; set; } = "";
		public DateTimeOffset CreatedAt { get; set; }
		public DateTimeOffset UpdatedAt { get; set; }
		public bool Locked { get; set; }
		public bool Visible { get; set; }
	}
}
