using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Objects.Events;
using Basil.Testing;
using static Basil.Testing.BeatmapsetWorld;

namespace Basil.Application.Beatmaps.Tests.Objects;

/// <summary>
///     Importing an uploaded beatmapset archive, as <c>Beatmapsets.ImportAsync</c> documents it: an unreadable or
///     locked archive is rejected and nothing of it is kept, an accepted one adds or replaces the beatmapset it
///     belongs to, and one <see cref="BeatmapsetImported" /> says so.
/// </summary>
public sealed class BeatmapsetsImportTests
{
	private static readonly Diff Easy = new("Easy", "easy file");
	private static readonly Diff Hard = new("Hard", "hard file");
	private static readonly Diff Insane = new("Insane", "insane file");

	private static Task<BeatmapsetImportResult> ImportAsync(TestApplication app, byte[] archive)
	{
		return app.Beatmapsets.ImportAsync(new MemoryStream(archive), TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task ImportAsync_WithAReadableArchive_StoresTheSetWithEveryDifficulty()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();

		var result = await ImportAsync(app, Osz.Build(null, Easy, Hard));

		Assert.Null(result.Failure);
		Assert.NotNull(result.Set);
		Assert.Equal(["Easy", "Hard"], result.Beatmaps.Select(beatmap => beatmap.Metadata.Version).Order());
		Assert.Equal(new[] { Osz.HashOf(Easy), Osz.HashOf(Hard) }.Select(hash => hash.ToString()).Order(),
			result.Beatmaps.Select(beatmap => beatmap.Hash.ToString()).Order());
		Assert.Equal(result.Set, Assert.Single(world.Sets.Records.Values));
		Assert.Equal("Artist - Title", result.Set.Metadata.Name);
	}

	[Fact]
	public async Task ImportAsync_WithAReadableArchive_PublishesOneBeatmapsetImportedWithTheStoredSet()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();

		var result = await ImportAsync(app, Osz.Build(null, Easy, Hard));

		var imported = Assert.IsType<BeatmapsetImported>(Assert.Single(app.BeatmapsetEvents.Events));
		Assert.Equal(result.Set, imported.Set);
		Assert.Equal(result.Beatmaps, imported.Beatmaps);
	}

	[Fact]
	public async Task ImportAsync_WithAReadableArchive_KeepsTheArchiveAsTheSetsOwnAndNothingElse()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		var archive = Osz.Build(null, Easy);

		var result = await ImportAsync(app, archive);

		Assert.Equal(archive, world.Storage.Stored[result.Set!.Id].Archive.Bytes);
		Assert.Empty(world.Storage.Uploaded);
	}

	[Fact]
	public async Task ImportAsync_WithAReadableArchive_MakesTheFilesTheSetServes()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();

		var result = await ImportAsync(app, Osz.Build(null, Easy, Hard));

		var files = world.Storage.Stored[result.Set!.Id];
		Assert.Contains((BeatmapsetFileType.Version, $"{Osz.HashOf(Easy)}.osu"), files.Files);
		Assert.Contains((BeatmapsetFileType.Version, $"{Osz.HashOf(Hard)}.osu"), files.Files);
		Assert.True(files.WithoutVideo);
	}

	[Fact]
	public async Task ImportAsync_WithDeclaredVideos_MakesTheArchiveWithoutVideoWithoutExactlyThoseFiles()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		var withVideo = new Diff("Easy", "easy file", Video: "intro.ogv");
		var sharing = new Diff("Hard", "hard file", Video: "intro.ogv");
		var plain = new Diff("Insane", "insane file");

		var result = await ImportAsync(app, Osz.Build(null, withVideo, sharing, plain));

		Assert.Equal(["intro.ogv"], world.Storage.Stored[result.Set!.Id].VideoFiles);
	}

	[Fact]
	public async Task ImportAsync_WithADeclaredSetId_GivesTheNewSetThatId()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();

		var result = await ImportAsync(app, Osz.Build(12345, Easy));

		Assert.Equal(12345, result.Set!.Id);
		Assert.False(result.Set.IsLocallyIngested);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-5)]
	public async Task ImportAsync_WithADeclaredSetIdThatIsNotPositive_GivesTheNewSetALocalId(int declared)
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();

		var result = await ImportAsync(app, Osz.Build(declared, Easy));

		Assert.Null(result.Failure);
		Assert.True(result.Set!.IsLocallyIngested);
	}

	[Fact]
	public async Task ImportAsync_WithoutReplacing_RejectsTheArchiveOfAStoredSetAndKeepsTheStoredOne()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		var first = await ImportAsync(app, Osz.Build(100, Easy));

		var again = await app.Beatmapsets.ImportAsync(new MemoryStream(Osz.Build(100, Easy, Hard)), false,
			TestContext.Current.CancellationToken);

		Assert.Equal(BeatmapsetImportFailure.AlreadyExists, again.Failure);
		Assert.Null(again.Set);
		var stored = Assert.Single(world.Sets.Records.Values);
		Assert.Equal(first.Set, stored);
		Assert.Single(world.Beatmaps.Records);
	}

	[Fact]
	public async Task ImportAsync_WithoutReplacing_StoresAnArchiveOfANewSet()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();

		var result = await app.Beatmapsets.ImportAsync(new MemoryStream(Osz.Build(100, Easy)), false,
			TestContext.Current.CancellationToken);

		Assert.Null(result.Failure);
		Assert.Equal(100, result.Set!.Id);
	}

	[Fact]
	public async Task ImportAsync_WithoutADeclaredSetId_GivesTheNewSetALocalId()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();

		var result = await ImportAsync(app, Osz.Build(null, Easy));

		Assert.True(result.Set!.IsLocallyIngested);
	}

	[Fact]
	public async Task ImportAsync_WithADeclaredBeatmapId_GivesTheNewBeatmapThatId()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();

		var result = await ImportAsync(app, Osz.Build(null, Easy with { OnlineId = 777 }));

		Assert.Equal(777, Assert.Single(result.Beatmaps).Id);
	}

	[Fact]
	public async Task ImportAsync_WithAnArchiveThatIsNotReadable_IsRejectedAsUnreadableAndKeepsNothing()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();

		var result = await ImportAsync(app, [1, 2, 3, 4]);

		Assert.Equal(BeatmapsetImportFailure.Unreadable, result.Failure);
		Assert.Null(result.Set);
		Assert.Empty(result.Beatmaps);
		Assert.Empty(world.Sets.Records);
		Assert.Empty(world.Beatmaps.Records);
		Assert.Empty(world.Storage.Stored);
		Assert.Empty(world.Storage.Uploaded);
		Assert.Empty(app.BeatmapsetEvents.Events);
	}

	[Fact]
	public async Task ImportAsync_WithAnEmptyArchive_IsRejectedAsUnreadable()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();

		var result = await ImportAsync(app, []);

		Assert.Equal(BeatmapsetImportFailure.Unreadable, result.Failure);
		Assert.Empty(app.BeatmapsetEvents.Events);
	}

	[Fact]
	public async Task ImportAsync_WithAnArchiveThatRepeatsADifficulty_IsRejectedAsUnreadable()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();

		var result = await ImportAsync(app, Osz.Build(null, Easy, Easy with { Version = "Copy" }));

		Assert.Equal(BeatmapsetImportFailure.Unreadable, result.Failure);
		Assert.Empty(world.Sets.Records);
		Assert.Empty(app.BeatmapsetEvents.Events);
	}

	[Fact]
	public async Task ImportAsync_OfTheSameArchiveAgain_ReplacesTheSetAndPublishesAgain()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		var first = await ImportAsync(app, Osz.Build(null, Easy, Hard));

		var second = await ImportAsync(app, Osz.Build(null, Easy, Hard));

		Assert.Equal(first.Set, second.Set);
		Assert.Single(world.Sets.Records);
		Assert.Equal(2, world.Beatmaps.Records.Count);
		Assert.Equal(2, app.BeatmapsetEvents.Events.OfType<BeatmapsetImported>().Count());
	}

	[Fact]
	public async Task ImportAsync_OfANewVersionSharingADifficulty_ReplacesTheSetThatHasIt()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		var first = await ImportAsync(app, Osz.Build(null, Easy, Hard));

		var second = await ImportAsync(app, Osz.Build(null, Easy, Insane));

		Assert.Equal(first.Set, second.Set);
		Assert.Single(world.Sets.Records);
		Assert.Equal(["Easy", "Insane"], second.Beatmaps.Select(beatmap => beatmap.Metadata.Version).Order());
	}

	[Fact]
	public async Task ImportAsync_OfANewVersion_RemovesTheDifficultiesItNoLongerHas()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await ImportAsync(app, Osz.Build(null, Easy, Hard));

		await ImportAsync(app, Osz.Build(null, Easy, Insane));

		Assert.DoesNotContain(Osz.HashOf(Hard), world.Beatmaps.Records.Keys);
		Assert.Contains(Osz.HashOf(Insane), world.Beatmaps.Records.Keys);
	}

	[Fact]
	public async Task ImportAsync_OfAnArchiveWithTheDeclaredIdOfAStoredSet_ReplacesThatSet()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		var first = await ImportAsync(app, Osz.Build(555, Easy));

		var second = await ImportAsync(app, Osz.Build(555, Hard));

		Assert.Equal(first.Set, second.Set);
		Assert.Single(world.Sets.Records);
		Assert.Equal("Hard", Assert.Single(second.Beatmaps).Metadata.Version);
	}

	[Fact]
	public async Task ImportAsync_OfASetThatAlreadyHasADifficulty_PrefersThatSetToTheDeclaredId()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		var first = await ImportAsync(app, Osz.Build(100, Easy));

		var second = await ImportAsync(app, Osz.Build(200, Easy, Hard));

		Assert.Equal(first.Set!.Id, second.Set!.Id);
		Assert.Equal(100, second.Set.Id);
	}

	[Fact]
	public async Task ImportAsync_KeepsTheCreationTimeOfAReplacedSet()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		var first = await ImportAsync(app, Osz.Build(null, Easy));
		var created = first.Set!.CreatedAt;

		var second = await ImportAsync(app, Osz.Build(null, Easy, Hard));

		Assert.Equal(created, second.Set!.CreatedAt);
	}

	[Fact]
	public async Task ImportAsync_OfALockedSet_IsRejectedAsLockedAndChangesNothing()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		var first = await ImportAsync(app, Osz.Build(null, Easy));
		first.Set!.Locked = true;

		var second = await ImportAsync(app, Osz.Build(null, Easy, Hard));

		Assert.Equal(BeatmapsetImportFailure.Locked, second.Failure);
		Assert.Null(second.Set);
		Assert.Equal(1, world.Beatmaps.Records.Count);
		Assert.Single(app.BeatmapsetEvents.Events);
		Assert.Empty(world.Storage.Uploaded);
	}

	[Fact]
	public async Task ImportAsync_ThatWouldRemoveALockedDifficulty_IsRejectedAsLocked()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		var first = await ImportAsync(app, Osz.Build(null, Easy, Hard));
		world.Beatmaps.Records[Osz.HashOf(Hard)].Locked = true;

		var second = await ImportAsync(app, Osz.Build(null, Easy));

		Assert.Equal(BeatmapsetImportFailure.Locked, second.Failure);
		Assert.Equal(2, world.Beatmaps.Records.Count);
		Assert.Equal(first.Set, Assert.Single(world.Sets.Records.Values));
	}

	[Fact]
	public async Task ImportAsync_ThatChangesTheFileOfALockedDifficulty_IsRejectedAsLocked()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await ImportAsync(app, Osz.Build(null, Easy with { OnlineId = 9001 }, Hard));
		world.Beatmaps.Records[Osz.HashOf(Easy)].Locked = true;

		var second = await ImportAsync(app,
			Osz.Build(null, Easy with { OnlineId = 9001, Content = "easy file, edited" }, Hard));

		Assert.Equal(BeatmapsetImportFailure.Locked, second.Failure);
		Assert.Contains(Osz.HashOf(Easy), world.Beatmaps.Records.Keys);
	}

	[Fact]
	public async Task ImportAsync_ThatKeepsALockedDifficultyUnchanged_IsAccepted()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await ImportAsync(app, Osz.Build(null, Easy, Hard));
		world.Beatmaps.Records[Osz.HashOf(Easy)].Locked = true;

		var second = await ImportAsync(app, Osz.Build(null, Easy, Insane));

		Assert.Null(second.Failure);
		Assert.True(world.Beatmaps.Records[Osz.HashOf(Easy)].Locked);
	}

	[Fact]
	public async Task ImportAsync_OfADifficultyOfALockedSetInAnotherSet_IsRejectedAsLocked()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		var first = await ImportAsync(app, Osz.Build(100, Easy));
		first.Set!.Locked = true;

		// The archive declares another set but holds a difficulty the locked set has: it belongs to the locked set.
		var second = await ImportAsync(app, Osz.Build(200, Easy, Hard));

		Assert.Equal(BeatmapsetImportFailure.Locked, second.Failure);
		Assert.DoesNotContain(200, world.Sets.Records.Keys);
	}

	[Fact]
	public async Task ImportAsync_OfAnArchiveThatTakesADifficultyFromALockedSet_IsRejectedAsLocked()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		await ImportAsync(app, Osz.Build(100, Easy));
		var other = await ImportAsync(app, Osz.Build(200, Hard));
		other.Set!.Locked = true;

		// The archive belongs to set 100 through Easy, but Hard is a difficulty of the locked set 200.
		var result = await ImportAsync(app, Osz.Build(null, Easy, Hard));

		Assert.Equal(BeatmapsetImportFailure.Locked, result.Failure);
		Assert.Equal(200, world.Beatmaps.Records[Osz.HashOf(Hard)].Beatmapset.Id);
	}

	[Fact]
	public async Task ImportAsync_LeavesTheArchiveStreamOpen()
	{
		var world = new BeatmapsetWorld();
		using var app = world.CreateApplication();
		using var stream = new MemoryStream(Osz.Build(null, Easy));

		await app.Beatmapsets.ImportAsync(stream, TestContext.Current.CancellationToken);

		Assert.True(stream.CanRead);
	}
}