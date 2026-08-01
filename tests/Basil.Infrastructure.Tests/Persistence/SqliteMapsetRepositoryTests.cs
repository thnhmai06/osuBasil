using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Tests.Persistence;

public class SqliteMapsetRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
	private readonly SqliteBeatmapRepository _beatmapRepository =
		new(fixture.ConnectionString, NullLogger<SqliteBeatmapRepository>.Instance);

	private readonly SqliteMapsetRepository _mapsetRepository =
		new(fixture.ConnectionString, NullLogger<SqliteMapsetRepository>.Instance);

	private static Mapset MakeMapset(int id, bool isPrivate = false)
	{
		return new Mapset(id, "Camellia", "Exit This Earth's Atomosphere", "cmyui",
			new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
			IsPrivate: isPrivate);
	}

	[Fact]
	public async Task UpsertThenFetchById_ReturnsMapset()
	{
		var mapset = MakeMapset(9001);

		await _mapsetRepository.UpsertAsync(mapset);
		var fetched = await _mapsetRepository.FetchByIdAsync(mapset.Id);

		Assert.Equal(mapset, fetched);
	}

	[Fact]
	public async Task FetchById_Unknown_ReturnsNull()
	{
		Assert.Null(await _mapsetRepository.FetchByIdAsync(-1));
	}

	[Fact]
	public async Task Upsert_ExistingId_ReplacesRow()
	{
		var mapset = MakeMapset(9002);
		await _mapsetRepository.UpsertAsync(mapset);

		var updated = mapset with { Artist = "Updated Artist" };
		await _mapsetRepository.UpsertAsync(updated);

		var fetched = await _mapsetRepository.FetchByIdAsync(mapset.Id);
		Assert.Equal("Updated Artist", fetched!.Artist);
	}

	[Fact]
	public async Task DeleteAsync_CascadesToBeatmaps()
	{
		var mapset = MakeMapset(9003);
		await _mapsetRepository.UpsertAsync(mapset);

		var beatmap = new Beatmap(new string('z', 32), 9003001, mapset, "Hyper", "z.osu",
			new Difficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0, 8.0, 5.0, 6.5),
			new OsuBeatmapObjectCounts { MaxCombo = 500 });
		await _beatmapRepository.UpsertAsync(beatmap);

		await _mapsetRepository.DeleteAsync(mapset.Id);

		Assert.Null(await _mapsetRepository.FetchByIdAsync(mapset.Id));
		Assert.Null(await _beatmapRepository.FetchOneAsync(beatmap.Id, includePrivate: true));
	}

	[Fact]
	public async Task FetchMaxIdAsync_ReturnsHighestId()
	{
		await _mapsetRepository.UpsertAsync(MakeMapset(9010));
		await _mapsetRepository.UpsertAsync(MakeMapset(9011));

		var maxId = await _mapsetRepository.FetchMaxIdAsync();

		Assert.True(maxId >= 9011);
	}

	[Fact]
	public async Task FetchAllIdsAsync_IncludesUpsertedIds()
	{
		await _mapsetRepository.UpsertAsync(MakeMapset(9020));
		await _mapsetRepository.UpsertAsync(MakeMapset(9021));

		var ids = await _mapsetRepository.FetchAllIdsAsync();

		Assert.Contains(9020, ids);
		Assert.Contains(9021, ids);
	}

	[Fact]
	public async Task SetFrozenAsync_TogglesIsFrozen()
	{
		var mapset = MakeMapset(9030);
		await _mapsetRepository.UpsertAsync(mapset);

		await _mapsetRepository.SetFrozenAsync(mapset.Id, true);
		Assert.True((await _mapsetRepository.FetchByIdAsync(mapset.Id))!.IsFrozen);

		await _mapsetRepository.SetFrozenAsync(mapset.Id, false);
		Assert.False((await _mapsetRepository.FetchByIdAsync(mapset.Id))!.IsFrozen);
	}

	[Fact]
	public async Task FetchPageAsync_OnlyWithVisibleBeatmaps_ExcludesPrivateMapsets()
	{
		var visible = MakeMapset(9040);
		var privateOnly = MakeMapset(9041, true);
		await _mapsetRepository.UpsertAsync(visible);
		await _mapsetRepository.UpsertAsync(privateOnly);
		await _beatmapRepository.UpsertAsync(new Beatmap(new string('y', 32), 9040001, visible, "Hyper", "y.osu",
			new Difficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0, 8.0, 5.0, 6.5),
			new OsuBeatmapObjectCounts { MaxCombo = 500 }));
		await _beatmapRepository.UpsertAsync(new Beatmap(new string('x', 32), 9041001, privateOnly, "Hyper", "x.osu",
			new Difficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0, 8.0, 5.0, 6.5),
			new OsuBeatmapObjectCounts { MaxCombo = 500 }));

		var visibleOnly = await _mapsetRepository.FetchPageAsync(0, 100, true);
		var everything = await _mapsetRepository.FetchPageAsync(0, 100, false);

		Assert.Contains(visibleOnly, m => m.Id == 9040);
		Assert.DoesNotContain(visibleOnly, m => m.Id == 9041);
		Assert.Contains(everything, m => m.Id == 9040);
		Assert.Contains(everything, m => m.Id == 9041);
	}

	[Fact]
	public async Task Upsert_ExistingFrozenMapset_ReingestionDoesNotClearFreeze()
	{
		var mapset = MakeMapset(9031);
		await _mapsetRepository.UpsertAsync(mapset);
		await _mapsetRepository.SetFrozenAsync(mapset.Id, true);

		var reingested = await _mapsetRepository.UpsertAsync(mapset with { Artist = "Re-ingested Artist" });

		Assert.True(reingested.IsFrozen);
		Assert.True((await _mapsetRepository.FetchByIdAsync(mapset.Id))!.IsFrozen);
	}

	[Fact]
	public async Task SetPrivateAsync_TogglesIsPrivate()
	{
		var mapset = MakeMapset(9032);
		await _mapsetRepository.UpsertAsync(mapset);

		await _mapsetRepository.SetPrivateAsync(mapset.Id, true);
		Assert.True((await _mapsetRepository.FetchByIdAsync(mapset.Id))!.IsPrivate);

		await _mapsetRepository.SetPrivateAsync(mapset.Id, false);
		Assert.False((await _mapsetRepository.FetchByIdAsync(mapset.Id))!.IsPrivate);
	}

	[Fact]
	public async Task Upsert_ExistingPrivateMapset_ReingestionDoesNotClearPrivate()
	{
		var mapset = MakeMapset(9033);
		await _mapsetRepository.UpsertAsync(mapset);
		await _mapsetRepository.SetPrivateAsync(mapset.Id, true);

		var reingested = await _mapsetRepository.UpsertAsync(mapset with { Artist = "Re-ingested Artist" });

		Assert.True(reingested.IsPrivate);
		Assert.True((await _mapsetRepository.FetchByIdAsync(mapset.Id))!.IsPrivate);
	}

	[Fact]
	public async Task SetBackgroundFileAsync_SetsAndClearsValue()
	{
		var mapset = MakeMapset(9034);
		await _mapsetRepository.UpsertAsync(mapset);

		await _mapsetRepository.SetBackgroundFileAsync(mapset.Id, "bg.jpg");
		Assert.Equal("bg.jpg", (await _mapsetRepository.FetchByIdAsync(mapset.Id))!.BackgroundFile);

		await _mapsetRepository.SetBackgroundFileAsync(mapset.Id, null);
		Assert.Null((await _mapsetRepository.FetchByIdAsync(mapset.Id))!.BackgroundFile);
	}

	[Fact]
	public async Task Upsert_ExistingBackgroundFile_ReingestionDoesNotClearIt()
	{
		var mapset = MakeMapset(9035);
		await _mapsetRepository.UpsertAsync(mapset);
		await _mapsetRepository.SetBackgroundFileAsync(mapset.Id, "bg.jpg");

		var reingested = await _mapsetRepository.UpsertAsync(mapset with { Artist = "Re-ingested Artist" });

		Assert.Equal("bg.jpg", reingested.BackgroundFile);
		Assert.Equal("bg.jpg", (await _mapsetRepository.FetchByIdAsync(mapset.Id))!.BackgroundFile);
	}
}