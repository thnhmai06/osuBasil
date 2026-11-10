using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Beatmaps.Persistence;

/// <summary>Stores beatmap difficulties in the <c>beatmaps</c> table.</summary>
internal sealed class EfBeatmapRepository(DatabaseSession database) : IBeatmapRepository
{
	// The objects of each mode are stored with a mode discriminator that need not come first.
	private static readonly JsonSerializerOptions StoredJson = new()
	{
		AllowOutOfOrderMetadataProperties = true,
		TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { StoredObjects } }
	};

	/// <inheritdoc />
	public Task UpdateAsync(BeatmapRecord beatmap, CancellationToken cancellationToken = default)
	{
		var row = ToRow(beatmap);
		return database.WriteAsync((BeatmapsDbContext context, CancellationToken token) =>
			context.UpsertAsync(row, stored => stored.Id == row.Id, token), cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask<BeatmapRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		var row = await database.ReadAsync((BeatmapsDbContext context) => context.Beatmaps
			.Include(b => b.Set)
			.FirstOrDefaultAsync(b => b.Id == id, cancellationToken), cancellationToken);
		return row is null ? null : ToBeatmap(row, row.Set.ToRecord());
	}

	/// <inheritdoc />
	public async ValueTask<BeatmapRecord?> GetAsync(Md5 hash, CancellationToken cancellationToken = default)
	{
		var row = await database.ReadAsync((BeatmapsDbContext context) => context.Beatmaps
			.Include(b => b.Set)
			.Where(b => b.Hash == hash.HashValue)
			.FirstOrDefaultAsync(cancellationToken), cancellationToken);
		return row is null ? null : ToBeatmap(row, row.Set.ToRecord());
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<BeatmapRecord>> ListAsync(BeatmapsetRecord set,
		CancellationToken cancellationToken = default)
	{
		var rows = await database.ReadAsync((BeatmapsDbContext context) => context.Beatmaps
			.Where(b => b.BeatmapsetId == set.Id)
			.OrderBy(b => b.Id)
			.Take(ListLimits.Ceiling)
			.ToListAsync(cancellationToken), cancellationToken);
		return [.. rows.Select(row => ToBeatmap(row, set))];
	}

	/// <inheritdoc />
	public async Task<Page<BeatmapRecord>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var fetched = await database.ReadAsync((BeatmapsDbContext context) => BeatmapQueryFilter
			.Apply(context.Beatmaps, query)
			.Include(b => b.Set)
			.OrderByDescending(b => b.BeatmapsetId)
			.ThenBy(b => b.Star)
			.ThenBy(b => b.Id)
			.Skip(page.Offset)
			.Take(page.Fetch)
			.ToListAsync(cancellationToken), cancellationToken);

		var (rows, hasNext) = page.Slice(fetched);
		return new Page<BeatmapRecord>([.. rows.Select(row => ToBeatmap(row, row.Set.ToRecord()))], hasNext);
	}

	/// <inheritdoc />
	public Task RetainAsync(BeatmapsetRecord set, IReadOnlyCollection<BeatmapRecord> keep,
		CancellationToken cancellationToken = default)
	{
		var setId = set.Id;
		var keepIds = keep.Select(beatmap => beatmap.Id).ToArray();
		return database.WriteAsync((BeatmapsDbContext context, CancellationToken token) => context.Beatmaps
			.Where(b => b.BeatmapsetId == setId && !((IEnumerable<int>)keepIds).Contains(b.Id))
			.ExecuteDeleteAsync(token), cancellationToken);
	}

	/// <summary>Stores the difficulties of a beatmapset as a scan or import brings them in.</summary>
	/// <param name="set">The beatmapset the difficulties belong to.</param>
	/// <param name="data">Every difficulty the set has, in order.</param>
	/// <param name="now">The time of the import, in UTC.</param>
	/// <param name="releasable">The ids of the sets whose difficulties this commit replaces or removes.</param>
	/// <param name="context">The EF context the commit runs on.</param>
	/// <param name="token">A token that cancels the commit.</param>
	/// <returns>Each stored difficulty, in the same order.</returns>
	/// <remarks>
	///     A difficulty already stored under the same file's hash, or under the same osu! id, is moved to this set and
	///     keeps its id, its creation time and whether it is locked or shown, when its set is replaced or removed by the
	///     same commit; otherwise it is refused.
	/// </remarks>
	internal static async Task<List<BeatmapRecord>> StoreAsync(BeatmapsetRecord set,
		IReadOnlyList<BeatmapData> data, DateTimeOffset now, IReadOnlySet<int> releasable, BeatmapsDbContext context,
		CancellationToken token)
	{
		var stored = new List<BeatmapRecord>(data.Count);
		foreach (var item in data) stored.Add(await StoreOneAsync(set, item, now, releasable, context, token));

		return stored;
	}

	private static async Task<BeatmapRecord> StoreOneAsync(BeatmapsetRecord set, BeatmapData item, DateTimeOffset now,
		IReadOnlySet<int> releasable, BeatmapsDbContext context, CancellationToken token)
	{
		var row = await FindAsync(item, context, token);
		if (row is not null && row.BeatmapsetId != set.Id && !releasable.Contains(row.BeatmapsetId))
			throw new AlreadyExistsException(
				$"A difficulty of another beatmapset already has the hash {item.Hash} or the id {row.Id}.");

		var knownId = row?.Id ?? item.OnlineId;
		// A record needs an id; the row of a difficulty without an osu! id has none until a local id is taken.
		var toStore = ToRow(Build(set, item, row, now, knownId ?? 1));
		if (knownId is null)
			toStore.Id = 0;

		if (toStore.Id == 0)
		{
			// A difficulty without an osu! id takes the next local id.
			toStore.Id = (int)await IdCounters.NextAsync(context, BeatmapsDbContext.LocalBeatmapIds, token);
			context.Beatmaps.Add(toStore);
			await context.SaveChangesAsync(token);
		}
		else
		{
			await context.UpsertAsync(toStore, candidate => candidate.Id == toStore.Id, token);
		}

		return Build(set, item, row, now, toStore.Id);
	}

	private static BeatmapRecord Build(BeatmapsetRecord set, BeatmapData item, BeatmapRow? row, DateTimeOffset now,
		int id)
	{
		return new BeatmapRecord
		{
			Id = id,
			Beatmapset = set,
			Hash = item.Hash,
			Metadata = item.Metadata,
			Assets = item.Assets,
			Locked = row?.Locked ?? false,
			Visible = row?.Visible ?? true,
			CreatedAt = row?.CreatedAt ?? now,
			UpdatedAt = now
		};
	}

	private static async Task<BeatmapRow?> FindAsync(BeatmapData data, BeatmapsDbContext context,
		CancellationToken token)
	{
		var hash = data.Hash.HashValue;
		var row = await context.Beatmaps.FirstOrDefaultAsync(b => b.Hash == hash, token);
		if (row is not null || data.OnlineId is not { } onlineId)
			return row;

		return await context.Beatmaps.FirstOrDefaultAsync(b => b.Id == onlineId, token);
	}

	/// <summary>Builds a beatmap from a stored row.</summary>
	private static BeatmapRecord ToBeatmap(BeatmapRow row, BeatmapsetRecord set)
	{
		var objects = row.Objects.Deserialize<BeatmapObjects>(StoredJson)
		              ?? BeatmapObjects.Empty((GameMode)row.Mode);

		return new BeatmapRecord
		{
			Id = row.Id,
			Beatmapset = set,
			Hash = new Md5(row.Hash),
			Metadata = new BeatmapMetadata
			{
				Version = row.Version,
				Difficulty = new BeatmapDifficulty((GameMode)row.Mode, row.Bpm, TimeSpan.FromMilliseconds(row.Length),
					row.Cs, row.Ar, row.Od, row.Hp, row.Star),
				Objects = objects
			},
			Assets = new BeatmapAssets
			{
				HasStoryboard = row.Storyboard,
				BackgroundFile = row.Background,
				AudioFile = row.Audio,
				VideoFile = row.Video,
				PreviewTime = row.PreviewTime is { } preview ? TimeSpan.FromMilliseconds(preview) : null
			},
			Locked = row.Locked,
			Visible = row.Visible,
			CreatedAt = row.CreatedAt,
			UpdatedAt = row.UpdatedAt
		};
	}

	/// <summary>Makes the row that stores a beatmap, with the values it has now.</summary>
	private static BeatmapRow ToRow(BeatmapRecord beatmap)
	{
		var difficulty = beatmap.Metadata.Difficulty;
		return new BeatmapRow
		{
			Id = beatmap.Id,
			BeatmapsetId = beatmap.Beatmapset.Id,
			Hash = beatmap.Hash.HashValue,
			Version = beatmap.Metadata.Version,
			Mode = (int)difficulty.Mode,
			Star = difficulty.Star,
			Length = (int)difficulty.Length.TotalMilliseconds,
			Bpm = difficulty.Bpm,
			Cs = difficulty.Cs,
			Ar = difficulty.Ar,
			Od = difficulty.Od,
			Hp = difficulty.Hp,
			Objects = JsonSerializer.SerializeToElement(beatmap.Metadata.Objects, StoredJson),
			Circles = (beatmap.Metadata.Objects as OsuObjects)?.Circles,
			Sliders = (beatmap.Metadata.Objects as OsuObjects)?.Sliders,
			Background = beatmap.Assets.BackgroundFile,
			Audio = beatmap.Assets.AudioFile,
			Video = beatmap.Assets.VideoFile,
			PreviewTime = (int?)beatmap.Assets.PreviewTime?.TotalMilliseconds,
			Storyboard = beatmap.Assets.HasStoryboard,
			CreatedAt = beatmap.CreatedAt.ToUniversalTime(),
			UpdatedAt = beatmap.UpdatedAt.ToUniversalTime(),
			Locked = beatmap.Locked,
			Visible = beatmap.Visible
		};
	}

	/// <summary>Stores the objects of each game mode under the mode's own discriminator.</summary>
	/// <param name="info">The type being described.</param>
	private static void StoredObjects(JsonTypeInfo info)
	{
		if (info.Type != typeof(BeatmapObjects)) return;

		info.PolymorphismOptions = new JsonPolymorphismOptions
		{
			TypeDiscriminatorPropertyName = "mode",
			DerivedTypes =
			{
				new JsonDerivedType(typeof(OsuObjects), (int)GameMode.Standard),
				new JsonDerivedType(typeof(TaikoObjects), (int)GameMode.Taiko),
				new JsonDerivedType(typeof(CatchObjects), (int)GameMode.Catch),
				new JsonDerivedType(typeof(ManiaObjects), (int)GameMode.Mania)
			}
		};
	}
}