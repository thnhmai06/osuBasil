using Basil.Application.Beatmaps.Objects.Events;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Testing;
using static Basil.Testing.BeatmapsetWorld;

namespace Basil.Application.Beatmaps.Tests.Objects;

/// <summary>
///     Scanning the beatmapset storage, as <c>Beatmapsets.ScanAsync</c> documents it: what is waiting in the storage is
///     taken in, stored sets stay in step with their archives, sets whose archive is gone are forgotten, and the
///     events say what changed, imports first.
/// </summary>
public sealed class BeatmapsetsScanTests
{
	private static readonly Diff Easy = new("Easy", "easy file");
	private static readonly Diff Hard = new("Hard", "hard file");
	private static readonly Diff Insane = new("Insane", "insane file");
	private static readonly DateTimeOffset Later = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

	private static Task<BeatmapsetScanResult> ScanAsync(TestApplication app)
	{
		return app.Beatmapsets.ScanAsync(TestContext.Current.CancellationToken);
	}

	private static Task<BeatmapsetScanResult> ScanAsync(TestApplication app, BeatmapsetSource entry)
	{
		return app.Beatmapsets.ScanAsync(entry, TestContext.Current.CancellationToken);
	}

	/// <summary>Takes one set into the storage by a scan and returns its id.</summary>
	private static async Task<int> TakeInAsync(BeatmapsetWorld world, TestApplication app, int id, params Diff[] diffs)
	{
		world.Storage.PlaceArchive($"{id}.osz", Osz.Build(id, diffs));
		var result = await ScanAsync(app);
		Assert.Equal(new BeatmapsetScanResult(1, 0, 0), result);
		return id;
	}

	#region The whole storage

	[Fact]
	public async Task EmptyStorage_ChangesNothing()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), result);
		Assert.Empty(app.BeatmapsetEvents.Events);
	}

	[Fact]
	public async Task LooseArchive_TakesAndImports()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		world.Storage.PlaceArchive("loose.osz", Osz.Build(10, Easy, Hard));

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(1, 0, 0), result);
		var imported = Assert.IsType<BeatmapsetImported>(Assert.Single(app.BeatmapsetEvents.Events));
		Assert.Equal(10, imported.Set.Id);
		Assert.Equal(2, imported.Beatmaps.Count);
		Assert.Equal(2, world.Beatmaps.Records.Count);
	}

	[Fact]
	public async Task LooseArchive_MovesIntoFolder()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		var archive = world.Storage.PlaceArchive("loose.osz", Osz.Build(10, Easy));

		await ScanAsync(app);

		Assert.Empty(world.Storage.Waiting);
		Assert.Same(archive, world.Storage.Stored[10].Archive);
		Assert.Contains((BeatmapsetFileType.Version, $"{Osz.HashOf(Easy)}.osu"), world.Storage.Stored[10].Files);
	}

	[Fact]
	public async Task SeveralArchives_PublishesInOrder()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		world.Storage.PlaceArchive("b.osz", Osz.Build(20, Hard));
		world.Storage.PlaceArchive("a.osz", Osz.Build(10, Easy));
		world.Storage.PlaceArchive("c.osz", Osz.Build(30, Insane));

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(3, 0, 0), result);
		Assert.Equal([20, 10, 30], app.BeatmapsetEvents.Events.OfType<BeatmapsetImported>().Select(e => e.Set.Id));
	}

	[Fact]
	public async Task UnreadableArchive_TakesNothingIn()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		world.Storage.PlaceArchive("broken.osz", [9, 9, 9]);

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), result);
		Assert.Empty(world.Sets.Records);
		Assert.Single(world.Storage.Waiting);
		Assert.Empty(app.BeatmapsetEvents.Events);
	}

	[Fact]
	public async Task MixedArchives_TakesReadableOnly()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		world.Storage.PlaceArchive("broken.osz", [9, 9, 9]);
		world.Storage.PlaceArchive("good.osz", Osz.Build(10, Easy));

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(1, 0, 0), result);
		Assert.Equal(10, Assert.IsType<BeatmapsetImported>(Assert.Single(app.BeatmapsetEvents.Events)).Set.Id);
	}

	[Fact]
	public async Task LooseFilesFolder_PacksIntoSet()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		world.Storage.PlaceLooseFolder("by hand", Osz.Build(10, Easy));

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(1, 0, 0), result);
		Assert.Equal(10, Assert.IsType<BeatmapsetImported>(Assert.Single(app.BeatmapsetEvents.Events)).Set.Id);
		Assert.Empty(world.Storage.Uploaded);
	}

	[Fact]
	public async Task NonBeatmapsetFolder_ChangesNothing()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		world.Storage.PlaceLooseFolder("not a set", [1, 2, 3]);

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), result);
		Assert.Empty(world.Sets.Records);
		Assert.Empty(world.Storage.Uploaded);
	}

	[Fact]
	public async Task EmptyPackingFolder_ChangesNothing()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		world.Storage.PlaceLooseFolder("empty", null!);

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), result);
		Assert.Empty(app.BeatmapsetEvents.Events);
	}

	[Fact]
	public async Task UnchangedStorage_ChangesNothing()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy);
		var opened = world.Storage.Stored[10].Archive.Opened;
		var events = app.BeatmapsetEvents.Events.Count;

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), result);
		Assert.Equal(opened, world.Storage.Stored[10].Archive.Opened);
		Assert.Equal(events, app.BeatmapsetEvents.Events.Count);
	}

	[Fact]
	public async Task ChangedArchive_UpdatesAndPublishes()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy);
		world.Storage.Overwrite(10, Osz.Build(10, Easy, Hard), Later);

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(0, 1, 0), result);
		var imported = Assert.IsType<BeatmapsetImported>(app.BeatmapsetEvents.Events[^1]);
		Assert.Equal(10, imported.Set.Id);
		Assert.Equal(2, imported.Beatmaps.Count);
		Assert.Equal(2, world.Beatmaps.Records.Count);
	}

	[Fact]
	public async Task ArchiveLostDifficulty_RemovesIt()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy, Hard);
		world.Storage.Overwrite(10, Osz.Build(10, Easy), Later);

		await ScanAsync(app);

		Assert.DoesNotContain(Osz.HashOf(Hard), world.Beatmaps.Records.Keys);
		Assert.Contains(Osz.HashOf(Easy), world.Beatmaps.Records.Keys);
	}

	[Fact]
	public async Task TouchedArchive_RemembersNewTime()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy);
		var events = app.BeatmapsetEvents.Events.Count;
		world.Storage.Overwrite(10, Osz.Build(10, Easy), Later);

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), result);
		Assert.Equal(events, app.BeatmapsetEvents.Events.Count);
		Assert.Equal(Later, world.Sets.Records[10].Fingerprint.Stamp.ModifiedAt);
	}

	[Fact]
	public async Task SetFolderGone_ForgetsAndPublishes()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy, Hard);
		var set = world.Sets.Records[10];
		world.Storage.RemoveFolder(10);

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(0, 0, 1), result);
		Assert.Equal(set, Assert.IsType<BeatmapsetDeleted>(app.BeatmapsetEvents.Events[^1]).Set);
		Assert.Empty(world.Sets.Records);
		Assert.Empty(world.Beatmaps.Records);
	}

	[Fact]
	public async Task SetArchiveUnreadable_ForgetsSet()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy);
		world.Storage.Overwrite(10, [1, 2, 3], Later);

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(0, 0, 1), result);
		Assert.IsType<BeatmapsetDeleted>(app.BeatmapsetEvents.Events[^1]);
		Assert.Empty(world.Sets.Records);
		Assert.Contains(10, world.Storage.Stored.Keys);
	}

	[Fact]
	public async Task ImportAndForget_PublishesImportFirst()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy);
		world.Storage.RemoveFolder(10);
		world.Storage.PlaceArchive("new.osz", Osz.Build(20, Hard));
		var before = app.BeatmapsetEvents.Events.Count;

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(1, 0, 1), result);
		var events = app.BeatmapsetEvents.Events.Skip(before).ToList();
		Assert.Equal(2, events.Count);
		Assert.Equal(20, Assert.IsType<BeatmapsetImported>(events[0]).Set.Id);
		Assert.Equal(10, Assert.IsType<BeatmapsetDeleted>(events[1]).Set.Id);
	}

	[Fact]
	public async Task LockedSetChanged_LeavesUntouched()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy);
		world.Sets.Records[10].Locked = true;
		var events = app.BeatmapsetEvents.Events.Count;
		world.Storage.Overwrite(10, Osz.Build(10, Easy, Hard), Later);

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), result);
		Assert.Contains(10, world.Sets.Records.Keys);
		Assert.Single(world.Beatmaps.Records);
		Assert.Equal(events, app.BeatmapsetEvents.Events.Count);
	}

	[Fact]
	public async Task DuplicateSetArchives_TakesFirstOnly()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		world.Storage.PlaceArchive("first.osz", Osz.Build(10, Easy));
		var second = world.Storage.PlaceArchive("second.osz", Osz.Build(10, Easy, Hard));

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(1, 0, 0), result);
		Assert.Single(app.BeatmapsetEvents.Events);
		Assert.Single(world.Beatmaps.Records);
		Assert.Contains(second, world.Storage.Waiting);
	}

	[Fact]
	public async Task ForeignArchiveDifficulty_UpdatesStoredSet()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy);
		world.Storage.PlaceArchive("update.osz", Osz.Build(99, Easy, Hard));

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(0, 1, 0), result);
		Assert.Equal([10], world.Sets.Records.Keys);
		Assert.Equal(2, world.Beatmaps.Records.Count);
	}

	[Fact]
	public async Task MissingSetFile_MakesItAgain()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy);
		world.Storage.Stored[10].Files.Clear();
		var events = app.BeatmapsetEvents.Events.Count;

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), result);
		Assert.Contains((BeatmapsetFileType.Version, $"{Osz.HashOf(Easy)}.osu"), world.Storage.Stored[10].Files);
		Assert.Equal(events, app.BeatmapsetEvents.Events.Count);
	}

	[Fact]
	public async Task SecondScan_ChangesNothing()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		world.Storage.PlaceArchive("a.osz", Osz.Build(10, Easy));
		world.Storage.PlaceLooseFolder("b", Osz.Build(20, Hard));
		await ScanAsync(app);
		var events = app.BeatmapsetEvents.Events.Count;

		var result = await ScanAsync(app);

		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), result);
		Assert.Equal(events, app.BeatmapsetEvents.Events.Count);
	}

	#endregion

	#region One entry

	[Fact]
	public async Task SingleLooseArchive_PublishesOneImport()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		var archive = world.Storage.PlaceArchive("loose.osz", Osz.Build(10, Easy));

		var result = await ScanAsync(app, archive);

		Assert.Equal(new BeatmapsetScanResult(1, 0, 0), result);
		Assert.Equal(10, Assert.IsType<BeatmapsetImported>(Assert.Single(app.BeatmapsetEvents.Events)).Set.Id);
	}

	[Fact]
	public async Task UnreadableLooseArchive_ChangesNothing()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		var archive = world.Storage.PlaceArchive("broken.osz", [1, 2]);

		var result = await ScanAsync(app, archive);

		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), result);
		Assert.Empty(app.BeatmapsetEvents.Events);
	}

	[Fact]
	public async Task UnchangedFolder_ChangesNothing()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy);
		var events = app.BeatmapsetEvents.Events.Count;

		var result = await ScanAsync(app, world.Storage.FolderOf(10));

		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), result);
		Assert.Equal(events, app.BeatmapsetEvents.Events.Count);
	}

	[Fact]
	public async Task ChangedFolder_UpdatesItsSet()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy);
		world.Storage.Overwrite(10, Osz.Build(10, Easy, Hard), Later);

		var result = await ScanAsync(app, world.Storage.FolderOf(10));

		Assert.Equal(new BeatmapsetScanResult(0, 1, 0), result);
		Assert.Equal(2, Assert.IsType<BeatmapsetImported>(app.BeatmapsetEvents.Events[^1]).Beatmaps.Count);
	}

	[Fact]
	public async Task
		IdNamedFolder_ReplacesStoredSet()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy);
		var archive = new MemoryArchive("10 renamed", Osz.Build(null, Insane), Later);

		var result = await ScanAsync(app, new MemoryFolder("10 renamed", 10, true, archive));

		Assert.Equal(new BeatmapsetScanResult(0, 1, 0), result);
		Assert.Equal([10], world.Sets.Records.Keys);
		Assert.Equal([Osz.HashOf(Insane).ToString()], world.Beatmaps.Records.Keys.Select(hash => hash.ToString()));
	}

	[Fact]
	public async Task RepeatedChangedFolder_ChangesNothing()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy);
		world.Storage.Overwrite(10, Osz.Build(10, Easy, Hard), Later);
		var folder = world.Storage.FolderOf(10);
		await ScanAsync(app, folder);

		var result = await ScanAsync(app, world.Storage.FolderOf(10));

		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), result);
	}

	[Fact]
	public async Task UnreadableFolderArchive_ForgetsSet()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy);
		world.Storage.Overwrite(10, [7], Later);

		var result = await ScanAsync(app, world.Storage.FolderOf(10));

		Assert.Equal(new BeatmapsetScanResult(0, 0, 1), result);
		Assert.Equal(10, Assert.IsType<BeatmapsetDeleted>(app.BeatmapsetEvents.Events[^1]).Set.Id);
		Assert.Empty(world.Sets.Records);
	}

	[Fact]
	public async Task GoneFolder_ForgetsAndPublishes()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy);
		world.Storage.RemoveFolder(10);

		var result = await ScanAsync(app, new MemoryFolder("10", 10, false, null));

		Assert.Equal(new BeatmapsetScanResult(0, 0, 1), result);
		Assert.Equal(10, Assert.IsType<BeatmapsetDeleted>(app.BeatmapsetEvents.Events[^1]).Set.Id);
		Assert.Empty(world.Sets.Records);
	}

	[Fact]
	public async Task ElsewhereArchiveGone_ChangesNothing()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy);
		var events = app.BeatmapsetEvents.Events.Count;

		var result = await ScanAsync(app, new MemoryFolder("10 old name", 10, false, null));

		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), result);
		Assert.Contains(10, world.Sets.Records.Keys);
		Assert.Equal(events, app.BeatmapsetEvents.Events.Count);
	}

	[Fact]
	public async Task IdlessGoneFolder_ChangesNothing()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();

		var withoutId = await ScanAsync(app, new MemoryFolder("no id", null, false, null));
		var unknownId = await ScanAsync(app, new MemoryFolder("77", 77, false, null));

		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), withoutId);
		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), unknownId);
		Assert.Empty(app.BeatmapsetEvents.Events);
	}

	[Fact]
	public async Task LooseFilesFolder_TakenIn()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		var folder = world.Storage.PlaceLooseFolder("by hand", Osz.Build(10, Easy));

		var result = await ScanAsync(app, folder);

		Assert.Equal(new BeatmapsetScanResult(1, 0, 0), result);
		Assert.Equal(10, Assert.IsType<BeatmapsetImported>(Assert.Single(app.BeatmapsetEvents.Events)).Set.Id);
		Assert.Empty(world.Storage.Uploaded);
	}

	[Fact]
	public async Task NonBeatmapsetFolder_KeepsNoArchive()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		var folder = world.Storage.PlaceLooseFolder("not a set", [1, 2, 3]);

		var result = await ScanAsync(app, folder);

		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), result);
		Assert.Empty(world.Sets.Records);
		Assert.Empty(world.Storage.Uploaded);
		Assert.Empty(app.BeatmapsetEvents.Events);
	}

	[Fact]
	public async Task LockedSetFolder_ChangesNothing()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await TakeInAsync(world, app, 10, Easy);
		world.Sets.Records[10].Locked = true;
		var events = app.BeatmapsetEvents.Events.Count;
		world.Storage.Overwrite(10, Osz.Build(10, Easy, Hard), Later);

		var result = await ScanAsync(app, world.Storage.FolderOf(10));

		Assert.Equal(new BeatmapsetScanResult(0, 0, 0), result);
		Assert.Single(world.Beatmaps.Records);
		Assert.Equal(events, app.BeatmapsetEvents.Events.Count);
	}

	#endregion
}