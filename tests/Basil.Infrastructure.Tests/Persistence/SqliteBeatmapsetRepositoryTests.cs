using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Tests.Persistence;

public class SqliteBeatmapsetRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
	private readonly SqliteBeatmapRepository _beatmapRepository =
		new(fixture.ConnectionString, NullLogger<SqliteBeatmapRepository>.Instance);

	private readonly SqliteBeatmapsetRepository _beatmapsetRepository =
		new(fixture.ConnectionString, NullLogger<SqliteBeatmapsetRepository>.Instance);

	private static Beatmapset MakeBeatmapset(int id, bool isPrivate = false)
	{
		return new Beatmapset(id, "Camellia", "Exit This Earth's Atomosphere", "cmyui",
			new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
			IsPrivate: isPrivate);
	}

	[Fact]
	public async Task UpsertThenFetchById_ReturnsBeatmapset()
	{
		var beatmapset = MakeBeatmapset(9001);

		await _beatmapsetRepository.UpsertAsync(beatmapset);
		var fetched = await _beatmapsetRepository.FetchByIdAsync(beatmapset.Id);

		Assert.Equal(beatmapset, fetched);
	}

	[Fact]
	public async Task FetchById_Unknown_ReturnsNull()
	{
		Assert.Null(await _beatmapsetRepository.FetchByIdAsync(-1));
	}

	[Fact]
	public async Task Upsert_ExistingId_ReplacesRow()
	{
		var beatmapset = MakeBeatmapset(9002);
		await _beatmapsetRepository.UpsertAsync(beatmapset);

		var updated = beatmapset with { Artist = "Updated Artist" };
		await _beatmapsetRepository.UpsertAsync(updated);

		var fetched = await _beatmapsetRepository.FetchByIdAsync(beatmapset.Id);
		Assert.Equal("Updated Artist", fetched!.Artist);
	}

	[Fact]
	public async Task DeleteAsync_CascadesToBeatmaps()
	{
		var beatmapset = MakeBeatmapset(9003);
		await _beatmapsetRepository.UpsertAsync(beatmapset);

		var beatmap = new Beatmap(new string('z', 32), 9003001, beatmapset, "Hyper", "z.osu",
			new Difficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0, 8.0, 5.0, 6.5),
			new OsuBeatmapObjectCounts { MaxCombo = 500 });
		await _beatmapRepository.UpsertAsync(beatmap);

		await _beatmapsetRepository.DeleteAsync(beatmapset.Id);

		Assert.Null(await _beatmapsetRepository.FetchByIdAsync(beatmapset.Id));
		Assert.Null(await _beatmapRepository.FetchOneAsync(beatmap.Id, includePrivate: true));
	}

	[Fact]
	public async Task FetchMaxIdAsync_ReturnsHighestId()
	{
		await _beatmapsetRepository.UpsertAsync(MakeBeatmapset(9010));
		await _beatmapsetRepository.UpsertAsync(MakeBeatmapset(9011));

		var maxId = await _beatmapsetRepository.FetchMaxIdAsync();

		Assert.True(maxId >= 9011);
	}

	[Fact]
	public async Task FetchAllIdsAsync_IncludesUpsertedIds()
	{
		await _beatmapsetRepository.UpsertAsync(MakeBeatmapset(9020));
		await _beatmapsetRepository.UpsertAsync(MakeBeatmapset(9021));

		var ids = await _beatmapsetRepository.FetchAllIdsAsync();

		Assert.Contains(9020, ids);
		Assert.Contains(9021, ids);
	}

	[Fact]
	public async Task SetFrozenAsync_TogglesIsFrozen()
	{
		var beatmapset = MakeBeatmapset(9030);
		await _beatmapsetRepository.UpsertAsync(beatmapset);

		await _beatmapsetRepository.SetFrozenAsync(beatmapset.Id, true);
		Assert.True((await _beatmapsetRepository.FetchByIdAsync(beatmapset.Id))!.IsFrozen);

		await _beatmapsetRepository.SetFrozenAsync(beatmapset.Id, false);
		Assert.False((await _beatmapsetRepository.FetchByIdAsync(beatmapset.Id))!.IsFrozen);
	}

	[Fact]
	public async Task FetchPageAsync_OnlyWithVisibleBeatmaps_ExcludesPrivateBeatmapsets()
	{
		var visible = MakeBeatmapset(9040);
		var privateOnly = MakeBeatmapset(9041, true);
		await _beatmapsetRepository.UpsertAsync(visible);
		await _beatmapsetRepository.UpsertAsync(privateOnly);
		await _beatmapRepository.UpsertAsync(new Beatmap(new string('y', 32), 9040001, visible, "Hyper", "y.osu",
			new Difficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0, 8.0, 5.0, 6.5),
			new OsuBeatmapObjectCounts { MaxCombo = 500 }));
		await _beatmapRepository.UpsertAsync(new Beatmap(new string('x', 32), 9041001, privateOnly, "Hyper", "x.osu",
			new Difficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0, 8.0, 5.0, 6.5),
			new OsuBeatmapObjectCounts { MaxCombo = 500 }));

		var visibleOnly = await _beatmapsetRepository.FetchPageAsync(0, 100, true);
		var everything = await _beatmapsetRepository.FetchPageAsync(0, 100, false);

		Assert.Contains(visibleOnly, m => m.Id == 9040);
		Assert.DoesNotContain(visibleOnly, m => m.Id == 9041);
		Assert.Contains(everything, m => m.Id == 9040);
		Assert.Contains(everything, m => m.Id == 9041);
	}

	[Fact]
	public async Task Upsert_ExistingFrozenBeatmapset_ReingestionDoesNotClearFreeze()
	{
		var beatmapset = MakeBeatmapset(9031);
		await _beatmapsetRepository.UpsertAsync(beatmapset);
		await _beatmapsetRepository.SetFrozenAsync(beatmapset.Id, true);

		var reingested = await _beatmapsetRepository.UpsertAsync(beatmapset with { Artist = "Re-ingested Artist" });

		Assert.True(reingested.IsFrozen);
		Assert.True((await _beatmapsetRepository.FetchByIdAsync(beatmapset.Id))!.IsFrozen);
	}

	[Fact]
	public async Task SetPrivateAsync_TogglesIsPrivate()
	{
		var beatmapset = MakeBeatmapset(9032);
		await _beatmapsetRepository.UpsertAsync(beatmapset);

		await _beatmapsetRepository.SetPrivateAsync(beatmapset.Id, true);
		Assert.True((await _beatmapsetRepository.FetchByIdAsync(beatmapset.Id))!.IsPrivate);

		await _beatmapsetRepository.SetPrivateAsync(beatmapset.Id, false);
		Assert.False((await _beatmapsetRepository.FetchByIdAsync(beatmapset.Id))!.IsPrivate);
	}

	[Fact]
	public async Task Upsert_ExistingPrivateBeatmapset_ReingestionDoesNotClearPrivate()
	{
		var beatmapset = MakeBeatmapset(9033);
		await _beatmapsetRepository.UpsertAsync(beatmapset);
		await _beatmapsetRepository.SetPrivateAsync(beatmapset.Id, true);

		var reingested = await _beatmapsetRepository.UpsertAsync(beatmapset with { Artist = "Re-ingested Artist" });

		Assert.True(reingested.IsPrivate);
		Assert.True((await _beatmapsetRepository.FetchByIdAsync(beatmapset.Id))!.IsPrivate);
	}

	[Fact]
	public async Task SetBackgroundFileAsync_SetsAndClearsValue()
	{
		var beatmapset = MakeBeatmapset(9034);
		await _beatmapsetRepository.UpsertAsync(beatmapset);

		await _beatmapsetRepository.SetBackgroundFileAsync(beatmapset.Id, "bg.jpg");
		Assert.Equal("bg.jpg", (await _beatmapsetRepository.FetchByIdAsync(beatmapset.Id))!.BackgroundFile);

		await _beatmapsetRepository.SetBackgroundFileAsync(beatmapset.Id, null);
		Assert.Null((await _beatmapsetRepository.FetchByIdAsync(beatmapset.Id))!.BackgroundFile);
	}

	[Fact]
	public async Task Upsert_ExistingBackgroundFile_ReingestionDoesNotClearIt()
	{
		var beatmapset = MakeBeatmapset(9035);
		await _beatmapsetRepository.UpsertAsync(beatmapset);
		await _beatmapsetRepository.SetBackgroundFileAsync(beatmapset.Id, "bg.jpg");

		var reingested = await _beatmapsetRepository.UpsertAsync(beatmapset with { Artist = "Re-ingested Artist" });

		Assert.Equal("bg.jpg", reingested.BackgroundFile);
		Assert.Equal("bg.jpg", (await _beatmapsetRepository.FetchByIdAsync(beatmapset.Id))!.BackgroundFile);
	}
}