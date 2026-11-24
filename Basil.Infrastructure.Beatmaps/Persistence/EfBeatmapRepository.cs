using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Exceptions;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Basil.Domain.Game;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Shared.Structures.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Beatmaps.Persistence;

internal sealed class EfBeatmapRepository(DatabaseSession database) : IBeatmapRepository
{
	// The objects of each mode are stored with a mode discriminator that need not come first.
	private static readonly JsonSerializerOptions StoredJson = new()
	{
		AllowOutOfOrderMetadataProperties = true,
		TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { StoredObjects } }
	};

	public Task UpdateAsync(BeatmapRecord beatmap)
	{
		var row = ToRow(beatmap);
		return database.WriteAsync((BeatmapsDbContext context) =>
			context.UpsertAsync(row, stored => stored.Id == row.Id));
	}

	public async ValueTask<BeatmapRecord?> GetAsync(int id, CancellationToken ct = default)
	{
		var row = await database.ReadAsync((BeatmapsDbContext context) => context.Beatmaps
			.Include(b => b.Set)
			.FirstOrDefaultAsync(b => b.Id == id, ct), ct);
		return row is null ? null : ToBeatmap(row, row.Set.ToRecord());
	}

	public async ValueTask<BeatmapRecord?> GetAsync(Md5Hash hash, CancellationToken ct = default)
	{
		var row = await database.ReadAsync((BeatmapsDbContext context) => context.Beatmaps
			.Include(b => b.Set)
			.Where(b => b.Hash == hash.HashValue)
			.FirstOrDefaultAsync(ct), ct);
		return row is null ? null : ToBeatmap(row, row.Set.ToRecord());
	}

	public async Task<IReadOnlyList<BeatmapRecord>> ListAsync(BeatmapsetRecord set,
		CancellationToken ct = default)
	{
		var rows = await database.ReadAsync((BeatmapsDbContext context) => context.Beatmaps
			.Where(b => b.BeatmapsetId == set.Id)
			.OrderBy(b => b.Id)
			.Take(ListLimits.Ceiling)
			.ToListAsync(ct), ct);
		return [.. rows.Select(row => ToBeatmap(row, set))];
	}

	public async Task<IReadOnlyList<BeatmapRecord>> ListAsync(IReadOnlyCollection<BeatmapsetRecord> sets,
		CancellationToken ct = default)
	{
		var bySet = sets.ToDictionary(set => set.Id);
		var ids = bySet.Keys.ToArray();
		var rows = await database.ReadAsync((BeatmapsDbContext context) => context.Beatmaps
			.Where(b => ids.Contains(b.BeatmapsetId))
			.OrderBy(b => b.Id)
			.Take(ListLimits.Ceiling)
			.ToListAsync(ct), ct);
		return [.. rows.Select(row => ToBeatmap(row, bySet[row.BeatmapsetId]))];
	}

	public async Task<IReadOnlyList<BeatmapRecord?>> ListAsync(IReadOnlyList<string> fileNames,
		CancellationToken ct = default)
	{
		var wanted = fileNames.Select(name => string.IsNullOrWhiteSpace(name) ? null : BeatmapFileName.Normalize(name))
			.ToList();
		var names = wanted.OfType<string>().Distinct().ToList();
		var rows = names.Count == 0
			? []
			: await database.ReadAsync((BeatmapsDbContext context) => context.Beatmaps
				.Include(b => b.Set)
				.Where(b => names.Contains(b.FileName))
				.OrderByDescending(b => b.BeatmapsetId)
				.ThenBy(b => b.Star)
				.ThenBy(b => b.Id)
				.Take(ListLimits.Ceiling)
				.ToListAsync(ct), ct);

		// The first row in the order of a single lookup wins when several difficulties share a file name.
		var first = rows.GroupBy(row => row.FileName).ToDictionary(group => group.Key, group => group.First());
		return [.. wanted.Select(name => name is not null && first.TryGetValue(name, out var row)
			? ToBeatmap(row, row.Set.ToRecord())
			: null)];
	}

	public async Task<Page<BeatmapRecord>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken ct = default)
	{
		var fetched = await database.ReadAsync((BeatmapsDbContext context) => BeatmapQueryFilter
			.Apply(context.Beatmaps, query)
			.Include(b => b.Set)
			.OrderByDescending(b => b.BeatmapsetId)
			.ThenBy(b => b.Star)
			.ThenBy(b => b.Id)
			.Skip(page.Offset)
			.Take(page.Fetch)
			.ToListAsync(ct), ct);

		var (rows, hasNext) = page.Slice(fetched);
		return new Page<BeatmapRecord>([.. rows.Select(row => ToBeatmap(row, row.Set.ToRecord()))], hasNext);
	}

	public async Task<IReadOnlyList<BeatmapRecord>> ListUncalculatedAsync(int afterId, int limit,
		CancellationToken ct = default)
	{
		var rows = await database.ReadAsync((BeatmapsDbContext context) => context.Beatmaps
			.Include(b => b.Set)
			.Where(b => b.Id > afterId && b.Star == null)
			.OrderBy(b => b.Id)
			.Take(limit)
			.ToListAsync(ct), ct);
		return [.. rows.Select(row => ToBeatmap(row, row.Set.ToRecord()))];
	}

	public Task RetainAsync(BeatmapsetRecord set, IReadOnlyCollection<BeatmapRecord> keep)
	{
		var setId = set.Id;
		var keepIds = keep.Select(beatmap => beatmap.Id).ToArray();
		return database.WriteAsync((BeatmapsDbContext context) => context.Beatmaps
			.Where(b => b.BeatmapsetId == setId && !((IEnumerable<int>)keepIds).Contains(b.Id))
			.ExecuteDeleteAsync());
	}

	internal static async Task<List<BeatmapRecord>> StoreAsync(BeatmapsetRecord set,
		IReadOnlyList<BeatmapData> data, DateTimeOffset now, IReadOnlySet<int> releasable, BeatmapsDbContext context)
	{
		var stored = new List<BeatmapRecord>(data.Count);
		foreach (var item in data) stored.Add(await StoreOneAsync(set, item, now, releasable, context));

		return stored;
	}

	private static async Task<BeatmapRecord> StoreOneAsync(BeatmapsetRecord set, BeatmapData item, DateTimeOffset now,
		IReadOnlySet<int> releasable, BeatmapsDbContext context)
	{
		var row = await FindAsync(item, context);
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
			toStore.Id = (int)await IdCounters.NextAsync(context, BeatmapsDbContext.LocalBeatmapIds);
			context.Beatmaps.Add(toStore);
			await context.SaveChangesAsync();
		}
		else
		{
			await context.UpsertAsync(toStore, candidate => candidate.Id == toStore.Id);
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
		CancellationToken token = default)
	{
		var hash = data.Hash.HashValue;
		var row = await context.Beatmaps.FirstOrDefaultAsync(b => b.Hash == hash, token);
		if (row is not null || data.OnlineId is not { } onlineId)
			return row;

		return await context.Beatmaps.FirstOrDefaultAsync(b => b.Id == onlineId, token);
	}

	private static BeatmapRecord ToBeatmap(BeatmapRow row, BeatmapsetRecord set)
	{
		var objects = row.ObjectCount.Deserialize<BeatmapObjectCount>(StoredJson)
		              ?? BeatmapObjectCount.Empty((GameMode)row.Mode);

		return new BeatmapRecord
		{
			Id = row.Id,
			Beatmapset = set,
			Hash = new Md5Hash(row.Hash),
			Metadata = new BeatmapMetadata
			{
				VersionName = row.Version,
				Mode = (GameMode)row.Mode,
				Difficulty = row is { Star: { } star, Length: { } length, Bpm: { } bpm, Cs: { } cs, Ar: { } ar, Od: { } od, Hp: { } hp }
					? new BeatmapDifficulty(bpm, TimeSpan.FromMilliseconds(length), cs, ar, od, hp, star)
					: null,
				ObjectCount = objects
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

	private static BeatmapRow ToRow(BeatmapRecord beatmap)
	{
		var difficulty = beatmap.Metadata.Difficulty;
		return new BeatmapRow
		{
			Id = beatmap.Id,
			BeatmapsetId = beatmap.Beatmapset.Id,
			Hash = beatmap.Hash.HashValue,
			Version = beatmap.Metadata.VersionName,
			FileName = BeatmapFileName.Of(beatmap.Beatmapset.Metadata.Artist, beatmap.Beatmapset.Metadata.Title,
				beatmap.Beatmapset.Metadata.Creator, beatmap.Metadata.VersionName),
			Mode = (int)beatmap.Metadata.Mode,
			Star = difficulty?.Star,
			Length = (int?)difficulty?.Length.TotalMilliseconds,
			Bpm = difficulty?.Bpm,
			Cs = difficulty?.Cs,
			Ar = difficulty?.Ar,
			Od = difficulty?.Od,
			Hp = difficulty?.Hp,
			ObjectCount = JsonSerializer.SerializeToElement(beatmap.Metadata.ObjectCount, StoredJson),
			Circles = (beatmap.Metadata.ObjectCount as OsuObjectCount)?.Circles,
			Sliders = (beatmap.Metadata.ObjectCount as OsuObjectCount)?.Sliders,
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

	private static void StoredObjects(JsonTypeInfo info)
	{
		if (info.Type != typeof(BeatmapObjectCount)) return;

		info.PolymorphismOptions = new JsonPolymorphismOptions
		{
			TypeDiscriminatorPropertyName = "mode",
			DerivedTypes =
			{
				new JsonDerivedType(typeof(OsuObjectCount), (int)GameMode.Standard),
				new JsonDerivedType(typeof(TaikoObjectCount), (int)GameMode.Taiko),
				new JsonDerivedType(typeof(CatchObjectCount), (int)GameMode.Catch),
				new JsonDerivedType(typeof(ManiaObjectCount), (int)GameMode.Mania)
			}
		};
	}
}