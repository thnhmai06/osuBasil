using System.IO.Compression;
using System.Text;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Beatmaps;
using Basil.Domain.Content;
using Basil.Domain.Cryptography;
using Basil.Domain.Shared;
using Basil.Infrastructure.Beatmaps.Storage;
using Basil.Testing.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Infrastructure.Beatmaps.Tests.Storage;

/// <summary>
///     Contract tests of FileBeatmapsetStorage, read through IBeatmapsetStorage. Scanning, fingerprint comparison and
///     indexing belong to the beatmapset service of Application.Runtime and are not tested here.
/// </summary>
public sealed class FileBeatmapsetStorageTests : IDisposable
{
	private static int _nextId = 1000;

	private readonly TempData _data = new();
	private readonly FileBeatmapsetStorage _storage;
	private readonly FakeTimeProvider _time = new();

	public FileBeatmapsetStorageTests()
	{
		_storage = new FileBeatmapsetStorage(_data.Paths, _time, NullLogger<FileBeatmapsetStorage>.Instance);
	}

	private string Sets => _data.Paths.Beatmapsets.Path;

	public void Dispose()
	{
		_data.Dispose();
	}

	#region Listing

	[Fact]
	public async Task EmptyStorage_ListsNothing()
	{
		Assert.Empty(await _storage.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task FoldersAndArchives_ListedWithNames()
	{
		PlaceFolder(1, "Artist1", "Title1", Osz(("a.osu", "x")));
		PlaceFolder(2, "Artist2", "Title2", Osz(("a.osu", "x")));
		PlaceArchive("3 Artist3 - Title3.osz", Osz(("a.osu", "x")));

		var sources = (await _storage.ListAsync(TestContext.Current.CancellationToken)).ToList();

		Assert.Equal(["1 Artist1 - Title1", "2 Artist2 - Title2", "3 Artist3 - Title3.osz"],
			sources.Select(source => source.Name).Order(StringComparer.Ordinal));
		Assert.Equal(2, sources.OfType<BeatmapsetFolder>().Count());
		Assert.Single(sources.OfType<BeatmapsetArchive>());
		Assert.All(sources.OfType<BeatmapsetFolder>(), folder => Assert.True(folder.Exists));
	}

	[Theory]
	[InlineData("123 Artist - Title", 123)]
	[InlineData("5 A - B", 5)]
	[InlineData("Artist - Title", null)]
	public async Task FolderName_CarriesBeatmapsetId(string name, int? id)
	{
		Directory.CreateDirectory(Path.Combine(Sets, name));

		var sources = await _storage.ListAsync(TestContext.Current.CancellationToken);

		var folder = Assert.IsAssignableFrom<BeatmapsetFolder>(Assert.Single(sources));
		Assert.Equal(id, folder.Id);
	}

	[Fact]
	public async Task FolderWithArchive_GivesIt()
	{
		var bytes = Osz(("a.osu", "x"));
		PlaceFolder(1, "With", "Archive", bytes);
		PlaceFolder(2, "Without", "Archive", null, ("a.osu", "x"));

		var folders = (await _storage.ListAsync(TestContext.Current.CancellationToken)).OfType<BeatmapsetFolder>()
			.ToDictionary(folder => folder.Id!.Value);

		Assert.NotNull(folders[1].Archive);
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await folders[1].Archive!.OpenAsync(TestContext.Current.CancellationToken)));
		Assert.Null(folders[2].Archive);
	}

	[Fact]
	public async Task ArchiveAndFolder_BothListed()
	{
		PlaceFolder(5, "Artist", "Title", Osz(("a.osu", "x")));
		PlaceArchive("5 Artist - Title.osz", Osz(("a.osu", "y")));

		var sources = await _storage.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(["5 Artist - Title", "5 Artist - Title.osz"],
			sources.Select(source => source.Name).Order(StringComparer.Ordinal));
	}

	[Fact]
	public async Task NonArchivesNested_NotSources()
	{
		File.WriteAllText(Path.Combine(Sets, "pack.zip"), "zip");
		File.WriteAllText(Path.Combine(Sets, "loose.osu"), "osu");
		File.WriteAllText(Path.Combine(Sets, "notes.txt"), "notes");
		File.WriteAllText(Path.Combine(Sets, "backup.osz.bak"), "bak");
		PlaceFolder(1, "Artist", "Title", Osz(("a.osu", "x")), ("nested/inner.osz", "nested"));

		var sources = await _storage.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(["1 Artist - Title"], sources.Select(source => source.Name));
	}

	[Fact]
	public async Task IncomingNames_NeverListed()
	{
		File.WriteAllBytes(Path.Combine(Sets, ".incoming_upload.osz"), Osz(("a.osu", "x")));
		Directory.CreateDirectory(Path.Combine(Sets, ".incoming_folder"));
		PlaceFolder(1, "Artist", "Title", Osz(("a.osu", "x")));

		var sources = await _storage.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(["1 Artist - Title"], sources.Select(source => source.Name));
	}

	[Fact]
	public async Task DeletedLikeFolder_OrdinaryEntry()
	{
		Directory.CreateDirectory(Path.Combine(Sets, "5 Artist - Title.deleted_0123"));
		Directory.CreateDirectory(Path.Combine(Sets, ".deleted_other"));

		var sources = await _storage.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal([".deleted_other", "5 Artist - Title.deleted_0123"],
			sources.Select(source => source.Name).Order(StringComparer.Ordinal));
	}

	[Fact]
	public async Task Archive_ListedWithSize()
	{
		var modified = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
		var original = Osz(("a.osu", "x"));
		var path = PlaceArchive("1 Artist - Title.osz", original);
		File.SetLastWriteTimeUtc(path, modified);
		var first = Assert.IsAssignableFrom<BeatmapsetArchive>(
			Assert.Single(await _storage.ListAsync(TestContext.Current.CancellationToken)));

		var replacement = Osz(("a.osu", "a much longer difficulty file"), ("b.osu", "and another one"));
		File.WriteAllBytes(path, replacement);
		File.SetLastWriteTimeUtc(path, modified.AddMinutes(5));
		var second =
			Assert.IsAssignableFrom<BeatmapsetArchive>(
				Assert.Single(await _storage.ListAsync(TestContext.Current.CancellationToken)));

		Assert.Equal(new ContentStamp(original.Length, new DateTimeOffset(modified)), first.Stamp);
		Assert.Equal(new ContentStamp(replacement.Length, new DateTimeOffset(modified.AddMinutes(5))), second.Stamp);
	}

	[Fact]
	public async Task ListedArchive_OpensFromStart()
	{
		var bytes = Osz(("a.osu", "x"));
		PlaceArchive("1 Artist - Title.osz", bytes);
		var archive =
			Assert.IsAssignableFrom<BeatmapsetArchive>(
				Assert.Single(await _storage.ListAsync(TestContext.Current.CancellationToken)));

		Assert.Equal(bytes, await ReadAllBytesAsync(await archive.OpenAsync(TestContext.Current.CancellationToken)));
		Assert.Equal(bytes, await ReadAllBytesAsync(await archive.OpenAsync(TestContext.Current.CancellationToken)));
		Assert.Equal(new Md5(bytes),
			(await archive.FingerprintAsync(TestContext.Current.CancellationToken)).Hash);
	}

	[Fact]
	public async Task CorruptArchive_ListedOthersShown()
	{
		var corrupt = Encoding.UTF8.GetBytes("this is not a zip file");
		PlaceArchive("99 Corrupt.osz", corrupt);
		PlaceFolder(1, "Artist", "Title", Osz(("a.osu", "x")));

		var sources = (await _storage.ListAsync(TestContext.Current.CancellationToken)).ToList();

		Assert.Equal(["1 Artist - Title", "99 Corrupt.osz"],
			sources.Select(source => source.Name).Order(StringComparer.Ordinal));
		var archive =
			Assert.IsAssignableFrom<BeatmapsetArchive>(sources.Single(source => source.Name == "99 Corrupt.osz"));
		Assert.Equal(new Md5(corrupt),
			(await archive.FingerprintAsync(TestContext.Current.CancellationToken)).Hash);
	}

	#endregion

	#region Uploading, packing and taking in

	[Fact]
	public async Task UploadedArchive_StoredNotListed()
	{
		var bytes = Osz(("a.osu", "x"));

		var archive = await _storage.SaveAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);

		Assert.Equal(bytes, await ReadAllBytesAsync(await archive.OpenAsync(TestContext.Current.CancellationToken)));
		Assert.Equal(bytes.Length, archive.Stamp.Size);
		Assert.Empty(await _storage.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task TwoUploads_StoredApart()
	{
		var first = await _storage.SaveAsync(new MemoryStream(Osz(("a.osu", "first"))),
			TestContext.Current.CancellationToken);
		var second = await _storage.SaveAsync(new MemoryStream(Osz(("a.osu", "second"))),
			TestContext.Current.CancellationToken);

		Assert.NotEqual(await ReadAllBytesAsync(await first.OpenAsync(TestContext.Current.CancellationToken)),
			await ReadAllBytesAsync(await second.OpenAsync(TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task FailedUpload_LeavesNothing()
	{
		await Assert.ThrowsAsync<IOException>(() =>
			_storage.SaveAsync(new TestStreams.Failing(), TestContext.Current.CancellationToken));

		Assert.Empty(Directory.GetFileSystemEntries(Sets));
	}

	[Fact]
	public async Task RemovedUpload_LeavesNothing()
	{
		var archive =
			await _storage.SaveAsync(new MemoryStream(Osz(("a.osu", "x"))), TestContext.Current.CancellationToken);

		await _storage.DeleteAsync(archive);

		Assert.Empty(Directory.GetFileSystemEntries(Sets));
	}

	[Fact]
	public async Task QuarantinedUpload_KeptInQuarantine()
	{
		var bytes = Osz(("a.osu", "x"));
		var archive = await _storage.SaveAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);

		await _storage.QuarantineAsync(archive);

		Assert.Empty(Directory.GetFileSystemEntries(Sets));
		var kept = Assert.Single(Directory.GetFiles(_data.Paths.Quarantine));
		Assert.Equal(bytes, await File.ReadAllBytesAsync(kept, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task TakenInUpload_BecomesSetArchive()
	{
		var bytes = Osz(("a.osu", "x"));
		var id = NewId();
		var set = Set(id, "Artist", "Title");
		var archive = await _storage.SaveAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);

		await _storage.SaveAsync(set, archive);

		Assert.Equal([$"{id} Artist - Title"], TopLevel());
		Assert.Equal(["archive.osz"], TempData.FilesUnder(Path.Combine(Sets, $"{id} Artist - Title")));
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task LooseArchiveTaken_LeavesStorage()
	{
		var bytes = Osz(("a.osu", "x"));
		var id = NewId();
		var set = Set(id, "Artist", "Title");
		PlaceArchive($"{id} Artist - Title.osz", bytes);
		var archive =
			Assert.IsAssignableFrom<BeatmapsetArchive>(
				Assert.Single(await _storage.ListAsync(TestContext.Current.CancellationToken)));

		await _storage.SaveAsync(set, archive);

		Assert.Equal([$"{id} Artist - Title"], TopLevel());
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task NewArchive_ReplacesFolder()
	{
		var id = NewId();
		var set = Set(id, "Artist", "Title");
		PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "old")), ("versions/stale.osu", "stale"));
		var bytes = Osz(("a.osu", "new"));
		var archive = await _storage.SaveAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);

		await _storage.SaveAsync(set, archive);

		Assert.Equal([$"{id} Artist - Title"], TopLevel());
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
		Assert.Null(await _storage.OpenAsync(set, BeatmapsetFileType.Version, "stale.osu",
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task ReplacedFolder_NoCopy()
	{
		var id = NewId();
		PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "old")), ("versions/stale.osu", "stale"));
		var archive = await _storage.SaveAsync(new MemoryStream(Osz(("a.osu", "new"))),
			TestContext.Current.CancellationToken);

		await _storage.SaveAsync(Set(id, "Artist", "Title"), archive);

		Assert.Equal([$"{id} Artist - Title", Path.Combine($"{id} Artist - Title", "archive.osz")], Everything());
	}

	[Fact]
	public async Task MovedSetFolder_OnlyTargetLeft()
	{
		var id = NewId();
		PlaceFolder(id, "Old Artist", "Old Title", Osz(("a.osu", "old")), ("versions/stale.osu", "stale"));
		var own = await OwnArchiveAsync($"{id} Old Artist - Old Title");
		PlaceFolder(id, "New Artist", "New Title", Osz(("a.osu", "other")), ("versions/stale2.osu", "stale"));

		await _storage.SaveAsync(Set(id, "New Artist", "New Title"), own);

		Assert.Equal([$"{id} New Artist - New Title"], TopLevel());
	}

	[Fact]
	public async Task NewArchive_DeletesOtherFolder()
	{
		var id = NewId();
		PlaceFolder(id, "Old Artist", "Old Title", Osz(("a.osu", "old")));
		var archive = await _storage.SaveAsync(new MemoryStream(Osz(("a.osu", "new"))),
			TestContext.Current.CancellationToken);

		await _storage.SaveAsync(Set(id, "New Artist", "New Title"), archive);

		Assert.Equal([$"{id} New Artist - New Title"], TopLevel());
	}

	[Fact]
	public async Task OwnArchiveKept_FolderRenamed()
	{
		var id = NewId();
		var bytes = Osz(("a.osu", "x"));
		PlaceFolder(id, "Old Artist", "Old Title", bytes, ("versions/hash.osu", "made from the archive"));
		var own = await OwnArchiveAsync($"{id} Old Artist - Old Title");
		var set = Set(id, "New Artist", "New Title");

		await _storage.SaveAsync(set, own);

		Assert.Equal([$"{id} New Artist - New Title"], TopLevel());
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
		Assert.Equal("made from the archive",
			await TestStreams.ReadAllAsync(await _storage.OpenAsync(set, BeatmapsetFileType.Version, "hash.osu",
				TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task CaseOnlyRename_FilesKept()
	{
		var id = NewId();
		var bytes = Osz(("a.osu", "x"));
		PlaceFolder(id, "artist", "title", bytes, ("versions/hash.osu", "made from the archive"));
		var own = await OwnArchiveAsync($"{id} artist - title");
		var set = Set(id, "Artist", "Title");

		await _storage.SaveAsync(set, own);

		Assert.Equal([$"{id} Artist - Title"], TopLevel());
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
		Assert.Equal("made from the archive",
			await TestStreams.ReadAllAsync(await _storage.OpenAsync(set, BeatmapsetFileType.Version, "hash.osu",
				TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task CaseOnlyRename_ReportsBoth()
	{
		var id = NewId();
		PlaceFolder(id, "artist", "title", Osz(("a.osu", "x")));
		var own = await OwnArchiveAsync($"{id} artist - title");
		await using var watching = Watching.Start(_storage, _time);

		await _storage.SaveAsync(Set(id, "Artist", "Title"), own);
		PlaceFolder(NewId(), "Sentinel", "Folder", Osz(("a.osu", "x")));
		var reported = await watching.ReadUntilAsync(source => source.Name.Contains("Sentinel"));

		Assert.All(reported.Where(source => !source.Name.Contains("Sentinel")),
			source => Assert.Contains(source.Name, new[] { $"{id} artist - title", $"{id} Artist - Title" }));
	}

	[Fact]
	public async Task LongMultibyteTitle_StoredAndFound()
	{
		var id = NewId();
		var set = Set(id, "Artist", new string('\u6f22', 100));
		var bytes = Osz(("a.osu", "x"));
		var archive = await _storage.SaveAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);

		await _storage.SaveAsync(set, archive);

		var name = Assert.Single(TopLevel());
		Assert.StartsWith($"{id} Artist - ", name);
		Assert.True(Encoding.UTF8.GetByteCount(name) <= 150,
			"A folder name stays well within the limit of common file systems.");
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task UnsafeFolderCharacters_StillFindsSet()
	{
		var id = NewId();
		var set = Set(id, "AC/DC", "What? \"Yes\" <no> | *maybe* : C:\\x...");
		var bytes = Osz(("a.osu", "x"));
		var archive = await _storage.SaveAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);

		await _storage.SaveAsync(set, archive);

		var name = Assert.Single(TopLevel());
		Assert.StartsWith($"{id} ", name);
		Assert.DoesNotContain(name, character => "<>:\"/\\|?*".Contains(character) || char.IsControl(character));
		Assert.False(name.EndsWith('.') || name.EndsWith(' '));
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task LongArtistTitle_StoredAndFound()
	{
		var id = NewId();
		var set = Set(id, new string('a', 300), new string('t', 300));
		var bytes = Osz(("a.osu", "x"));
		var archive = await _storage.SaveAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);

		await _storage.SaveAsync(set, archive);

		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task PackedFolder_ArchiveNotSource()
	{
		PlaceFolder(7, "Loose", "Folder", null, ("diff1.osu", "one"), ("diff2.osu", "two"), ("sb/bg.jpg", "jpg"));
		var folder = await FolderSourceAsync("7 Loose - Folder");

		var packed = await _storage.PackAsync(folder, TestContext.Current.CancellationToken);

		Assert.NotNull(packed);
		Assert.Equal(["diff1.osu", "diff2.osu", "sb/bg.jpg"],
			EntriesOf(await ReadAllBytesAsync(await packed.OpenAsync(TestContext.Current.CancellationToken))));
		Assert.Equal(["7 Loose - Folder"],
			(await _storage.ListAsync(TestContext.Current.CancellationToken)).Select(source => source.Name));
	}

	[Fact]
	public async Task Packing_KeepsLooseFiles()
	{
		PlaceFolder(7, "Loose", "Folder", null, ("diff1.osu", "one"), ("sb/bg.jpg", "jpg"));
		var folder = await FolderSourceAsync("7 Loose - Folder");

		await _storage.PackAsync(folder, TestContext.Current.CancellationToken);

		Assert.Equal(["diff1.osu", "sb/bg.jpg"], TempData.FilesUnder(Path.Combine(Sets, "7 Loose - Folder")));
	}

	[Fact]
	public async Task TakeInPacked_RemovesLooseFiles()
	{
		PlaceFolder(7, "Loose", "Folder", null, ("diff1.osu", "one"), ("sb/bg.jpg", "jpg"));
		var folder = await FolderSourceAsync("7 Loose - Folder");
		var packed = await _storage.PackAsync(folder, TestContext.Current.CancellationToken);
		Assert.NotNull(packed);
		var set = Set(7, "Loose", "Folder");

		await _storage.SaveAsync(set, packed);

		Assert.Equal(["7 Loose - Folder"], TopLevel());
		Assert.Equal(["archive.osz"], TempData.FilesUnder(Path.Combine(Sets, "7 Loose - Folder")));
		Assert.Equal(["diff1.osu", "sb/bg.jpg"],
			EntriesOf(await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true,
				TestContext.Current.CancellationToken))));
	}

	[Fact]
	public async Task RemovedPackedArchive_LeavesLooseFiles()
	{
		PlaceFolder(7, "Loose", "Folder", null, ("diff1.osu", "one"));
		var folder = await FolderSourceAsync("7 Loose - Folder");
		var packed = await _storage.PackAsync(folder, TestContext.Current.CancellationToken);
		Assert.NotNull(packed);

		await _storage.DeleteAsync(packed);

		Assert.Equal(["7 Loose - Folder"], TopLevel());
		Assert.Equal(["diff1.osu"], TempData.FilesUnder(Path.Combine(Sets, "7 Loose - Folder")));
	}

	[Fact]
	public async Task GoneFolder_PacksNull()
	{
		PlaceFolder(7, "Loose", "Folder", null, ("diff1.osu", "one"));
		var folder = await FolderSourceAsync("7 Loose - Folder");
		Directory.Delete(Path.Combine(Sets, "7 Loose - Folder"), true);

		Assert.Null(await _storage.PackAsync(folder, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task EmptyFolder_PacksNull()
	{
		Directory.CreateDirectory(Path.Combine(Sets, "7 Empty - Folder"));
		var folder = await FolderSourceAsync("7 Empty - Folder");

		Assert.Null(await _storage.PackAsync(folder, TestContext.Current.CancellationToken));
	}

	[Theory]
	[InlineData("versions/hash.osu")]
	[InlineData("archive-novideo.osz")]
	public async Task LostArchiveSet_PacksNullUnchanged(string madeFromTheArchive)
	{
		PlaceFolder(7, "Lost", "Archive", null, (madeFromTheArchive, "made from the archive"), ("diff1.osu", "one"));
		var folder = await FolderSourceAsync("7 Lost - Archive");

		Assert.Null(await _storage.PackAsync(folder, TestContext.Current.CancellationToken));
		Assert.Equal(new[] { "diff1.osu", madeFromTheArchive }.Order(StringComparer.Ordinal),
			TempData.FilesUnder(Path.Combine(Sets, "7 Lost - Archive")));
	}

	#endregion

	#region Files made from an archive

	[Theory]
	[InlineData(BeatmapsetFileType.Version, "d41d8cd98f00b204e9800998ecf8427e.osu",
		"versions/d41d8cd98f00b204e9800998ecf8427e.osu")]
	[InlineData(BeatmapsetFileType.Background, "bg.jpg", "backgrounds/bg.jpg")]
	[InlineData(BeatmapsetFileType.Audio, "song.mp3", "audio/song.mp3")]
	[InlineData(BeatmapsetFileType.Preview, "song.mp3", "audio/preview_song.mp3")]
	[InlineData(BeatmapsetFileType.Video, "intro.mp4", "video/intro.mp4")]
	[InlineData(BeatmapsetFileType.Storyboard, "ignored.osb", "storyboard.osb")]
	public async Task A_file_made_from_the_archive_is_stored_in_the_place_of_its_kind_and_reads_back(
		BeatmapsetFileType type, string name, string place)
	{
		var set = StoredSet(out var folder);

		await _storage.SaveAsync(set, type, name, TestStreams.Of("content"));

		Assert.Equal("content",
			await File.ReadAllTextAsync(Path.Combine(folder, place), TestContext.Current.CancellationToken));
		Assert.True(await _storage.ExistsAsync(set, type, name, TestContext.Current.CancellationToken));
		Assert.Equal("content",
			await TestStreams.ReadAllAsync(await _storage.OpenAsync(set, type, name,
				TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task Storyboard_StoredWhateverName()
	{
		var set = StoredSet(out var folder);

		await _storage.SaveAsync(set, BeatmapsetFileType.Storyboard, "", TestStreams.Of("storyboard"));

		Assert.Equal("storyboard",
			await File.ReadAllTextAsync(Path.Combine(folder, "storyboard.osb"), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task StoredStoryboard_FoundAndOpened()
	{
		var set = StoredSet(out var folder);
		await File.WriteAllTextAsync(Path.Combine(folder, "storyboard.osb"), "storyboard",
			TestContext.Current.CancellationToken);

		Assert.True(await _storage.ExistsAsync(set, BeatmapsetFileType.Storyboard, "",
			TestContext.Current.CancellationToken));
		Assert.Equal("storyboard",
			await TestStreams.ReadAllAsync(await _storage.OpenAsync(set, BeatmapsetFileType.Storyboard, "",
				TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task SameNameSave_ReplacesStoredFile()
	{
		var set = StoredSet(out _);
		await _storage.SaveAsync(set, BeatmapsetFileType.Background, "bg.jpg", TestStreams.Of("old and longer"));

		await _storage.SaveAsync(set, BeatmapsetFileType.Background, "bg.jpg", TestStreams.Of("new"));

		Assert.Equal("new",
			await TestStreams.ReadAllAsync(await _storage.OpenAsync(set, BeatmapsetFileType.Background, "bg.jpg",
				TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task OpenedFile_Seekable()
	{
		var set = StoredSet(out _);
		await _storage.SaveAsync(set, BeatmapsetFileType.Audio, "song.mp3", TestStreams.Of("content"));

		await using var stream = await _storage.OpenAsync(set, BeatmapsetFileType.Audio, "song.mp3",
			TestContext.Current.CancellationToken);

		Assert.NotNull(stream);
		Assert.True(stream.CanSeek);
	}

	[Fact]
	public async Task FailedSave_KeepsStoredFile()
	{
		var set = StoredSet(out var folder);
		await _storage.SaveAsync(set, BeatmapsetFileType.Background, "bg.jpg", TestStreams.Of("previous"));
		var before = TempData.FilesUnder(Sets);

		await Assert.ThrowsAsync<IOException>(() => _storage.SaveAsync(set, BeatmapsetFileType.Background, "bg.jpg",
			new TestStreams.Failing()));

		Assert.Equal(before, TempData.FilesUnder(Sets));
		Assert.Equal("previous",
			await File.ReadAllTextAsync(Path.Combine(folder, "backgrounds", "bg.jpg"),
				TestContext.Current.CancellationToken));
		Assert.Single(TopLevel());
	}

	[Theory]
	[InlineData("")]
	[InlineData("a/b.jpg")]
	[InlineData("a\\b.jpg")]
	[InlineData("../escape.jpg")]
	[InlineData("..\\escape.jpg")]
	[InlineData("C:\\escape.jpg")]
	[InlineData("bad?.jpg")]
	[InlineData("bad*.jpg")]
	[InlineData("trailing.")]
	[InlineData("trailing ")]
	[InlineData("CON")]
	[InlineData("con.jpg")]
	[InlineData("NUL.txt")]
	[InlineData("COM1")]
	[InlineData("LPT9.png")]
	public async Task NotPlainName_Refused(string name)
	{
		var set = StoredSet(out var folder);
		var before = TempData.FilesUnder(Sets);

		await Assert.ThrowsAnyAsync<ArgumentException>(() => _storage.SaveAsync(set, BeatmapsetFileType.Background,
			name, TestStreams.Of("content")));

		Assert.Equal(before, TempData.FilesUnder(Sets));
		Assert.False(File.Exists(Path.Combine(Sets, "escape.jpg")));
		Assert.False(File.Exists(Path.Combine(folder, "escape.jpg")));
	}

	[Theory]
	[InlineData("../archive.osz")]
	[InlineData("..\\archive.osz")]
	[InlineData("..")]
	[InlineData("")]
	[InlineData("CON")]
	public async Task UnsafeFileName_NotStored(string name)
	{
		var set = StoredSet(out _);

		Assert.False(await _storage.ExistsAsync(set, BeatmapsetFileType.Version, name,
			TestContext.Current.CancellationToken));
		Assert.Null(await _storage.OpenAsync(set, BeatmapsetFileType.Version, name,
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task UnstoredFile_Absent()
	{
		var set = StoredSet(out _);
		var before = Everything();

		Assert.False(await _storage.ExistsAsync(set, BeatmapsetFileType.Preview, "song.mp3",
			TestContext.Current.CancellationToken));
		Assert.Null(await _storage.OpenAsync(set, BeatmapsetFileType.Preview, "song.mp3",
			TestContext.Current.CancellationToken));
		Assert.Null(await _storage.OpenAsync(set, BeatmapsetFileType.Storyboard, "",
			TestContext.Current.CancellationToken));
		Assert.Null(await _storage.OpenArchiveAsync(set, false, TestContext.Current.CancellationToken));

		Assert.Equal(before, Everything());
	}

	[Fact]
	public async Task SetWithoutFolder_NoFiles()
	{
		var set = Set(NewId(), "Nobody", "Home");

		Assert.False(await _storage.ExistsAsync(set, BeatmapsetFileType.Background, "bg.jpg",
			TestContext.Current.CancellationToken));
		Assert.Null(await _storage.OpenAsync(set, BeatmapsetFileType.Background, "bg.jpg",
			TestContext.Current.CancellationToken));
		Assert.Null(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken));
		Assert.Null(await _storage.OpenArchiveAsync(set, false, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Archive_OpensEitherWay()
	{
		var id = NewId();
		var bytes = Osz(("a.osu", "x"));
		PlaceFolder(id, "Artist", "Title", bytes, ("archive-novideo.osz", "without video"));
		var set = Set(id, "Artist", "Title");

		await using var withVideo = await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken);
		await using var withoutVideo =
			await _storage.OpenArchiveAsync(set, false, TestContext.Current.CancellationToken);

		Assert.NotNull(withVideo);
		Assert.NotNull(withoutVideo);
		Assert.True(withVideo.CanSeek);
		Assert.True(withoutVideo.CanSeek);
		Assert.Equal(bytes.Length, withVideo.Length);
		Assert.Equal("without video".Length, withoutVideo.Length);
	}

	[Fact]
	public async Task WithoutVideo_KeepsNonVideos()
	{
		var id = NewId();
		var bytes = Osz(("a.osu", "difficulty"), ("bg.jpg", "background"), ("sub/song.mp3", "audio"),
			("video.mp4", "video"), ("sub/clip.avi", "clip"));
		PlaceFolder(id, "Artist", "Title", bytes);
		var set = Set(id, "Artist", "Title");

		await _storage.SaveArchiveWithoutVideoAsync(set, ["video.mp4", "sub/clip.avi"]);

		var made = await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, false,
			TestContext.Current.CancellationToken));
		Assert.Equal(["a.osu", "bg.jpg", "sub/song.mp3"], EntriesOf(made));
		Assert.Equal("audio", ReadEntry(made, "sub/song.mp3"));
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task WithoutVideo_DropsDeclaredVideo()
	{
		var id = NewId();
		PlaceFolder(id, "Artist", "Title",
			Osz(("a.osu", "difficulty"), ("Movie.OGV", "video"), ("sub\\intro.webm", "video")));
		var set = Set(id, "Artist", "Title");

		await _storage.SaveArchiveWithoutVideoAsync(set, ["movie.ogv", "sub/intro.webm"]);

		var made = await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, false,
			TestContext.Current.CancellationToken));
		Assert.Equal(["a.osu"], EntriesOf(made));
	}

	[Fact]
	public async Task UndeclaredVideoFile_Kept()
	{
		var id = NewId();
		PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "difficulty"), ("storyboard.mp4", "an animation")));
		var set = Set(id, "Artist", "Title");

		await _storage.SaveArchiveWithoutVideoAsync(set, []);

		var made = await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, false,
			TestContext.Current.CancellationToken));
		Assert.Equal(["a.osu", "storyboard.mp4"], EntriesOf(made));
	}

	[Fact]
	public async Task CorruptWithoutVideo_NotMade()
	{
		var id = NewId();
		var corrupt = Encoding.UTF8.GetBytes("this is not a zip file");
		PlaceFolder(id, "Artist", "Title", corrupt);
		var set = Set(id, "Artist", "Title");

		var failure = await Record.ExceptionAsync(() =>
			_storage.SaveArchiveWithoutVideoAsync(set, []));

		Assert.NotNull(failure);
		Assert.Null(await _storage.OpenArchiveAsync(set, false, TestContext.Current.CancellationToken));
		Assert.Equal(corrupt,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
		Assert.Equal([$"{id} Artist - Title"], TopLevel());
	}

	[Fact]
	public async Task ClearingSet_RemovesExtracted()
	{
		var id = NewId();
		var bytes = Osz(("a.osu", "x"));
		PlaceFolder(id, "Artist", "Title", bytes,
			("versions/hash.osu", "v"), ("backgrounds/bg.jpg", "b"), ("audio/song.mp3", "a"),
			("audio/preview_song.mp3", "p"),
			("video/intro.mp4", "m"), ("storyboard.osb", "s"), ("archive-novideo.osz", "n"));
		var set = Set(id, "Artist", "Title");

		await _storage.ClearAsync(set);

		Assert.Equal(["archive.osz"], TempData.FilesUnder(Path.Combine(Sets, $"{id} Artist - Title")));
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
		Assert.Equal([$"{id} Artist - Title"],
			(await _storage.ListAsync(TestContext.Current.CancellationToken)).Select(source => source.Name));
	}

	[Fact]
	public async Task ClearWithoutFolder_Nothing()
	{
		await _storage.ClearAsync(Set(NewId(), "Nobody", "Home"));

		Assert.Empty(Directory.GetFileSystemEntries(Sets));
	}

	#endregion

	#region Deleting

	[Fact]
	public async Task DeletedSet_LosesFolderOnly()
	{
		var id = NewId();
		var other = NewId();
		PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "x")), ("versions/hash.osu", "v"));
		PlaceFolder(other, "Other", "Set", Osz(("a.osu", "x")));

		await _storage.DeleteAsync(Set(id, "Artist", "Title"));

		Assert.Equal([$"{other} Other - Set"], TopLevel());
		Assert.Equal([$"{other} Other - Set"],
			(await _storage.ListAsync(TestContext.Current.CancellationToken)).Select(source => source.Name));
	}

	[Fact]
	public async Task DeletingSet_DeletesEveryFolder()
	{
		var id = NewId();
		PlaceFolder(id, "Old", "Name", Osz(("a.osu", "x")));
		PlaceFolder(id, "New", "Name", Osz(("a.osu", "y")));

		await _storage.DeleteAsync(Set(id, "New", "Name"));

		Assert.Empty(TopLevel());
	}

	[Fact]
	public async Task DeletingSet_NoRenamedLeftover()
	{
		var id = NewId();
		var other = NewId();
		PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "x")));
		PlaceFolder(other, "Other", "Set", Osz(("a.osu", "x")));
		var before = Everything().Where(path => path.StartsWith($"{other} ", StringComparison.Ordinal)).ToArray();

		await _storage.DeleteAsync(Set(id, "Artist", "Title"));

		Assert.Equal(before, Everything());
	}

	[Fact]
	public async Task NestedSetDeletion_RemovesAll()
	{
		var id = NewId();
		PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "x")), ("audio/preview_a.mp3", "p"), ("versions/h.osu", "v"));

		await _storage.DeleteAsync(Set(id, "Artist", "Title"));

		Assert.Empty(Everything());
	}

	[Fact]
	public async Task UnstoredSetDeletion_Nothing()
	{
		PlaceFolder(NewId(), "Artist", "Title", Osz(("a.osu", "x")));
		var before = Everything();

		await _storage.DeleteAsync(Set(NewId(), "Nobody", "Home"));

		Assert.Equal(before, Everything());
	}

	[Fact]
	public async Task FileInUse_DeletionFails()
	{
		Assert.SkipUnless(OperatingSystem.IsWindows(),
			"Only Windows refuses to delete a folder with an open file in it; no portable way to make a deletion fail.");
		var id = NewId();
		var bytes = Osz(("a.osu", "x"));
		var folder = PlaceFolder(id, "Artist", "Title", bytes);
		var set = Set(id, "Artist", "Title");
		var inUse = new FileStream(Path.Combine(folder, "archive.osz"), FileMode.Open, FileAccess.Read,
			FileShare.None);

		await Assert.ThrowsAsync<IOException>(() => _storage.DeleteAsync(set));
		await inUse.DisposeAsync();

		Assert.Equal([$"{id} Artist - Title"], TopLevel());
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task HandPlacedArchive_NotRemoved()
	{
		var path = PlaceArchive("1 Artist - Title.osz", Osz(("a.osu", "x")));
		var archive =
			Assert.IsAssignableFrom<BeatmapsetArchive>(
				Assert.Single(await _storage.ListAsync(TestContext.Current.CancellationToken)));

		await Assert.ThrowsAsync<ArgumentException>(() =>
			_storage.DeleteAsync(archive));

		Assert.True(File.Exists(path));
	}

	[Fact]
	public async Task OwnArchive_NotRemoved()
	{
		var id = NewId();
		var folder = PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "x")));
		var own = await OwnArchiveAsync($"{id} Artist - Title");

		await Assert.ThrowsAsync<ArgumentException>(() =>
			_storage.DeleteAsync(own));

		Assert.True(File.Exists(Path.Combine(folder, "archive.osz")));
	}

	#endregion

	#region Serialising operations on a set

	[Fact]
	public async Task SecondSetOperation_Waits()
	{
		var set = StoredSet(out _);
		var gated = new TestStreams.Gated("part ", "rest");
		var save = _storage.SaveAsync(set, BeatmapsetFileType.Background, "bg.jpg", gated);
		await gated.Started.WaitAsync(TestContext.Current.CancellationToken);

		var clear = _storage.ClearAsync(set);

		Assert.False(clear.IsCompleted);
		gated.Release();
		await save;
		await clear;
		Assert.False(await _storage.ExistsAsync(set, BeatmapsetFileType.Background, "bg.jpg",
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task OtherSetOperation_DoesNotWait()
	{
		var set = StoredSet(out _);
		var other = StoredSet(out _);
		var gated = new TestStreams.Gated("part ", "rest");
		var save = _storage.SaveAsync(set, BeatmapsetFileType.Background, "bg.jpg", gated);
		await gated.Started.WaitAsync(TestContext.Current.CancellationToken);

		await _storage.ClearAsync(other)
			.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

		Assert.False(save.IsCompleted);
		gated.Release();
		await save;
		Assert.True(await _storage.ExistsAsync(set, BeatmapsetFileType.Background, "bg.jpg",
			TestContext.Current.CancellationToken));
	}

	#endregion

	#region Watching

	[Fact]
	public async Task SettledChange_ReportsFolder()
	{
		var id = NewId();
		PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "x")));
		var replacement = Osz(("a.osu", "a longer difficulty file than before"));
		await using var watching = Watching.Start(_storage, _time);

		File.WriteAllBytes(Path.Combine(Sets, $"{id} Artist - Title", "archive.osz"), replacement);
		var reported = await watching.ReadUntilAsync(source => source.Name == $"{id} Artist - Title");

		var folder = Assert.IsAssignableFrom<BeatmapsetFolder>(reported[^1]);
		Assert.True(folder.Exists);
		Assert.Equal(id, folder.Id);
		Assert.Equal(replacement.Length, folder.Archive!.Stamp.Size);
	}

	[Fact]
	public async Task NewLooseArchive_ReportedAsArchive()
	{
		await using var watching = Watching.Start(_storage, _time);

		var bytes = Osz(("a.osu", "x"));
		PlaceArchive("9 New - Set.osz", bytes);
		var reported = await watching.ReadUntilAsync(source => source.Name == "9 New - Set.osz");

		var archive = Assert.IsAssignableFrom<BeatmapsetArchive>(reported[^1]);
		Assert.Equal(bytes.Length, archive.Stamp.Size);
	}

	[Fact]
	public async Task GoneFolder_ReportedMissing()
	{
		var id = NewId();
		PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "x")));
		await using var watching = Watching.Start(_storage, _time);

		Directory.Delete(Path.Combine(Sets, $"{id} Artist - Title"), true);
		var reported = await watching.ReadUntilAsync(source =>
			source is BeatmapsetFolder { Exists: false } && source.Name == $"{id} Artist - Title");

		var folder = Assert.IsAssignableFrom<BeatmapsetFolder>(reported[^1]);
		Assert.False(folder.Exists);
		Assert.Equal(id, folder.Id);
	}

	[Fact]
	public async Task Watching_StartsWhenAsked()
	{
		var id = NewId();
		PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "x")));
		var stop = new CancellationTokenSource();
		var reports = _storage.WatchAsync(stop.Token);
		File.WriteAllBytes(Path.Combine(Sets, $"{id} Artist - Title", "archive.osz"),
			Osz(("a.osu", "changed before anyone read the watch")));

		await using var watching = new Watching(_time, reports, stop);
		var reported = await watching.ReadUntilAsync(source => source.Name == $"{id} Artist - Title");

		Assert.NotEmpty(reported);
	}

	[Fact]
	public async Task IncomingNames_NeverReported()
	{
		await using var watching = Watching.Start(_storage, _time);

		File.WriteAllBytes(Path.Combine(Sets, ".incoming_upload.osz"), Osz(("a.osu", "x")));
		Directory.CreateDirectory(Path.Combine(Sets, ".incoming_folder"));
		File.WriteAllText(Path.Combine(Sets, ".incoming_folder", "archive.osz"), "inside");
		PlaceFolder(NewId(), "Sentinel", "Folder", Osz(("a.osu", "x")));
		var reported = await watching.ReadUntilAsync(source => source.Name.Contains("Sentinel"));

		Assert.DoesNotContain(reported,
			source => source.Name.StartsWith(".incoming_"));
	}

	[Fact]
	public async Task FolderVanishes_WatchContinues()
	{
		await using var watching = Watching.Start(_storage, _time);

		var gone = PlaceFolder(NewId(), "Gone", "Folder", Osz(("a.osu", "x")));
		Directory.Delete(gone, true);
		PlaceFolder(NewId(), "Stays", "Folder", Osz(("a.osu", "x")));
		var reported = await watching.ReadUntilAsync(source =>
			source.Name.Contains("Stays") && source is BeatmapsetFolder { Exists: true });

		Assert.All(reported.Where(source => source.Name.Contains("Gone")),
			source => Assert.False(Assert.IsAssignableFrom<BeatmapsetFolder>(source).Exists));
	}

	[Fact]
	public async Task CancelWatch_Ends()
	{
		await using var watching = Watching.Start(_storage, _time);

		var failure = await watching.CancelAndWaitAsync();

		Assert.True(failure is null or OperationCanceledException, failure?.ToString());
	}

	#endregion

	#region Helpers

	private static int NewId()
	{
		return Interlocked.Increment(ref _nextId);
	}

	private static BeatmapsetRecord Set(int id, string artist, string title)
	{
		return new BeatmapsetRecord
		{
			Id = id,
			Metadata = new BeatmapsetMetadata { Artist = artist, Title = title, Creator = "Mapper" },
			Fingerprint = new ContentFingerprint(new ContentStamp(0, DateTimeOffset.UnixEpoch),
				new Md5("00000000000000000000000000000000")),
			Assets = new BeatmapsetAssets(),
			UpdatedAt = DateTimeOffset.UnixEpoch,
			CreatedAt = DateTimeOffset.UnixEpoch
		};
	}

	/// <summary>Places a set folder by hand, as the server would have left it, and gives the set it holds.</summary>
	private BeatmapsetRecord StoredSet(out string folder)
	{
		var id = NewId();
		folder = PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "x")));
		return Set(id, "Artist", "Title");
	}

	/// <summary>Places a folder named <c>{id} {artist} - {title}</c> by hand, with an archive and files if given.</summary>
	private string PlaceFolder(int id, string artist, string title, byte[]? archive,
		params (string Name, string Content)[] files)
	{
		var folder = Path.Combine(Sets, $"{id} {artist} - {title}");
		Directory.CreateDirectory(folder);
		if (archive is not null) File.WriteAllBytes(Path.Combine(folder, "archive.osz"), archive);
		foreach (var (name, content) in files)
		{
			var path = Path.Combine(folder, name);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, content);
		}

		return folder;
	}

	private string PlaceArchive(string name, byte[] bytes)
	{
		var path = Path.Combine(Sets, name);
		File.WriteAllBytes(path, bytes);
		return path;
	}

	private string[] TopLevel()
	{
		return Directory.GetFileSystemEntries(Sets).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)
			.ToArray();
	}

	private string[] Everything()
	{
		return Directory.EnumerateFileSystemEntries(Sets, "*", SearchOption.AllDirectories)
			.Select(path => Path.GetRelativePath(Sets, path)).Order(StringComparer.Ordinal).ToArray();
	}

	private async Task<BeatmapsetFolder> FolderSourceAsync(string name)
	{
		return (await _storage.ListAsync(TestContext.Current.CancellationToken)).OfType<BeatmapsetFolder>()
			.Single(folder => folder.Name == name);
	}

	private async Task<BeatmapsetArchive> OwnArchiveAsync(string folderName)
	{
		var folder = await FolderSourceAsync(folderName);
		Assert.NotNull(folder.Archive);
		return folder.Archive;
	}

	private static byte[] Osz(params (string Name, string Content)[] entries)
	{
		using var memory = new MemoryStream();
		using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
		{
			foreach (var (name, content) in entries)
			{
				using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
				writer.Write(content);
			}
		}

		return memory.ToArray();
	}

	private static string[] EntriesOf(byte[] archive)
	{
		using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
		return zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToArray();
	}

	private static string ReadEntry(byte[] archive, string name)
	{
		using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
		using var reader = new StreamReader(zip.GetEntry(name)!.Open());
		return reader.ReadToEnd();
	}

	private static async Task<byte[]> ReadAllBytesAsync(Stream? stream)
	{
		Assert.NotNull(stream);
		await using (stream)
		{
			using var memory = new MemoryStream();
			await stream.CopyToAsync(memory, TestContext.Current.CancellationToken);
			return memory.ToArray();
		}
	}

	/// <summary>A running watch: reads the reports, moving the fake clock past the settling time while it waits for them.</summary>
	private sealed class Watching(
		FakeTimeProvider? time,
		IAsyncEnumerable<BeatmapsetSource> reports,
		CancellationTokenSource stop) : IAsyncDisposable
	{
		private readonly IAsyncEnumerator<BeatmapsetSource> _reports = reports.GetAsyncEnumerator(stop.Token);

		public async ValueTask DisposeAsync()
		{
			await stop.CancelAsync();
			await _reports.DisposeAsync();
			stop.Dispose();
		}

		public static Watching Start(FileBeatmapsetStorage storage, FakeTimeProvider? time)
		{
			var stop = new CancellationTokenSource();
			return new Watching(time, storage.WatchAsync(stop.Token), stop);
		}

		/// <summary>Reads reports until one matches.</summary>
		/// <returns>Every report read, the matching one last.</returns>
		public async Task<List<BeatmapsetSource>> ReadUntilAsync(Func<BeatmapsetSource, bool> match)
		{
			var seen = new List<BeatmapsetSource>();
			while (true)
			{
				var next = _reports.MoveNextAsync().AsTask();
				await Eventually.TrueAsync(() =>
				{
					time?.Advance(TimeSpan.FromSeconds(3));
					return next.IsCompleted;
				}, "a change was reported");
				Assert.True(await next, "The watch ended.");
				seen.Add(_reports.Current);
				if (match(_reports.Current)) return seen;
			}
		}

		/// <summary>Stops the watch while a read is waiting for a report.</summary>
		/// <returns>What the waiting read failed with, or <see langword="null" /> when it ended quietly.</returns>
		public async Task<Exception?> CancelAndWaitAsync()
		{
			var next = _reports.MoveNextAsync().AsTask();
			await stop.CancelAsync();
			return await Record.ExceptionAsync(() =>
				next.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
		}
	}

	#endregion
}