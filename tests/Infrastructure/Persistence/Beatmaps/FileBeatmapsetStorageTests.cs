using System.IO.Compression;
using System.Text;
using Basil.Application.Persistence.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Content;
using Basil.Infrastructure.Persistence.Beatmaps;
using Basil.Infrastructure.Persistence.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Infrastructure.Persistence.Tests.Beatmaps;

/// <summary>
///     Contract tests of FileBeatmapsetStorage, read through IBeatmapsetStorage. Scanning, fingerprint comparison and
///     indexing belong to the beatmapset service of Application.Runtime and are not tested here.
/// </summary>
public sealed class FileBeatmapsetStorageTests : IDisposable
{
	private static int _nextId = 1000;

	private readonly TempData _data = new();
	private readonly FakeTimeProvider _time = new();
	private readonly FileBeatmapsetStorage _storage;

	public FileBeatmapsetStorageTests()
	{
		_storage = new FileBeatmapsetStorage(_data.Paths, _time, NullLogger<FileBeatmapsetStorage>.Instance);
	}

	private string Sets => _data.Paths.Beatmapsets.Path;

	// ---- Listing ----------------------------------------------------------------------------------------------

	[Fact]
	public async Task Nothing_is_listed_when_the_storage_is_empty()
	{
		Assert.Empty(await _storage.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Set_folders_and_loose_archives_are_listed_with_their_names()
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
	public async Task A_folder_carries_the_beatmapset_id_its_name_starts_with(string name, int? id)
	{
		Directory.CreateDirectory(Path.Combine(Sets, name));

		var sources = await _storage.ListAsync(TestContext.Current.CancellationToken);

		var folder = Assert.IsAssignableFrom<BeatmapsetFolder>(Assert.Single(sources));
		Assert.Equal(id, folder.Id);
	}

	[Fact]
	public async Task A_folder_holding_an_archive_gives_it_and_one_without_gives_none()
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
	public async Task An_archive_and_a_folder_of_the_same_set_are_both_listed()
	{
		PlaceFolder(5, "Artist", "Title", Osz(("a.osu", "x")));
		PlaceArchive("5 Artist - Title.osz", Osz(("a.osu", "y")));

		var sources = await _storage.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(["5 Artist - Title", "5 Artist - Title.osz"],
			sources.Select(source => source.Name).Order(StringComparer.Ordinal));
	}

	[Fact]
	public async Task Files_that_are_not_archives_and_archives_nested_in_folders_are_not_sources()
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
	public async Task Names_marked_incoming_are_never_listed()
	{
		File.WriteAllBytes(Path.Combine(Sets, ".incoming_upload.osz"), Osz(("a.osu", "x")));
		Directory.CreateDirectory(Path.Combine(Sets, ".incoming_folder"));
		PlaceFolder(1, "Artist", "Title", Osz(("a.osu", "x")));

		var sources = await _storage.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(["1 Artist - Title"], sources.Select(source => source.Name));
	}

	[Fact]
	public async Task A_folder_named_like_a_deleted_one_is_an_ordinary_entry()
	{
		Directory.CreateDirectory(Path.Combine(Sets, "5 Artist - Title.deleted_0123"));
		Directory.CreateDirectory(Path.Combine(Sets, ".deleted_other"));

		var sources = await _storage.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal([".deleted_other", "5 Artist - Title.deleted_0123"],
			sources.Select(source => source.Name).Order(StringComparer.Ordinal));
	}

	[Fact]
	public async Task An_archive_is_listed_with_its_size_and_modification_time_and_a_replaced_one_with_the_new_ones()
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
	public async Task A_listed_archive_opens_from_its_start_each_time_and_fingerprints_its_bytes()
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
	public async Task A_corrupt_archive_is_listed_as_it_is_and_does_not_hide_the_other_sources()
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

	// ---- Uploading, packing and taking in ----------------------------------------------------------------------

	[Fact]
	public async Task An_uploaded_archive_is_stored_byte_for_byte_and_is_not_listed()
	{
		var bytes = Osz(("a.osu", "x"));

		var archive = await _storage.SaveAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);

		Assert.Equal(bytes, await ReadAllBytesAsync(await archive.OpenAsync(TestContext.Current.CancellationToken)));
		Assert.Equal(bytes.Length, archive.Stamp.Size);
		Assert.Empty(await _storage.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Two_uploaded_archives_are_stored_apart()
	{
		var first = await _storage.SaveAsync(new MemoryStream(Osz(("a.osu", "first"))),
			TestContext.Current.CancellationToken);
		var second = await _storage.SaveAsync(new MemoryStream(Osz(("a.osu", "second"))),
			TestContext.Current.CancellationToken);

		Assert.NotEqual(await ReadAllBytesAsync(await first.OpenAsync(TestContext.Current.CancellationToken)),
			await ReadAllBytesAsync(await second.OpenAsync(TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task An_upload_that_fails_leaves_nothing_in_the_storage()
	{
		await Assert.ThrowsAsync<IOException>(() =>
			_storage.SaveAsync(new TestStreams.Failing(), TestContext.Current.CancellationToken));

		Assert.Empty(Directory.GetFileSystemEntries(Sets));
	}

	[Fact]
	public async Task A_removed_upload_leaves_nothing_in_the_storage()
	{
		var archive =
			await _storage.SaveAsync(new MemoryStream(Osz(("a.osu", "x"))), TestContext.Current.CancellationToken);

		await _storage.DeleteAsync(archive, TestContext.Current.CancellationToken);

		Assert.Empty(Directory.GetFileSystemEntries(Sets));
	}

	[Fact]
	public async Task A_taken_in_upload_becomes_the_archive_of_a_set_folder_named_after_the_set()
	{
		var bytes = Osz(("a.osu", "x"));
		var id = NewId();
		var set = Set(id, "Artist", "Title");
		var archive = await _storage.SaveAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);

		await _storage.SaveAsync(set, archive, TestContext.Current.CancellationToken);

		Assert.Equal([$"{id} Artist - Title"], TopLevel());
		Assert.Equal(["archive.osz"], TempData.FilesUnder(Path.Combine(Sets, $"{id} Artist - Title")));
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task A_taken_in_loose_archive_leaves_its_place_in_the_storage()
	{
		var bytes = Osz(("a.osu", "x"));
		var id = NewId();
		var set = Set(id, "Artist", "Title");
		PlaceArchive($"{id} Artist - Title.osz", bytes);
		var archive =
			Assert.IsAssignableFrom<BeatmapsetArchive>(
				Assert.Single(await _storage.ListAsync(TestContext.Current.CancellationToken)));

		await _storage.SaveAsync(set, archive, TestContext.Current.CancellationToken);

		Assert.Equal([$"{id} Artist - Title"], TopLevel());
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task A_new_archive_replaces_the_folder_already_stored_under_the_same_name_with_nothing_stale()
	{
		var id = NewId();
		var set = Set(id, "Artist", "Title");
		PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "old")), ("versions/stale.osu", "stale"));
		var bytes = Osz(("a.osu", "new"));
		var archive = await _storage.SaveAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);

		await _storage.SaveAsync(set, archive, TestContext.Current.CancellationToken);

		Assert.Equal([$"{id} Artist - Title"], TopLevel());
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
		Assert.Null(await _storage.OpenAsync(set, BeatmapsetFileType.Version, "stale.osu",
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_replaced_folder_leaves_no_set_aside_copy_in_the_storage()
	{
		var id = NewId();
		PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "old")), ("versions/stale.osu", "stale"));
		var archive = await _storage.SaveAsync(new MemoryStream(Osz(("a.osu", "new"))),
			TestContext.Current.CancellationToken);

		await _storage.SaveAsync(Set(id, "Artist", "Title"), archive, TestContext.Current.CancellationToken);

		Assert.Equal([$"{id} Artist - Title", Path.Combine($"{id} Artist - Title", "archive.osz")], Everything());
	}

	[Fact]
	public async Task A_set_folder_moved_over_an_existing_target_leaves_only_the_target()
	{
		var id = NewId();
		PlaceFolder(id, "Old Artist", "Old Title", Osz(("a.osu", "old")), ("versions/stale.osu", "stale"));
		var own = await OwnArchiveAsync($"{id} Old Artist - Old Title");
		PlaceFolder(id, "New Artist", "New Title", Osz(("a.osu", "other")), ("versions/stale2.osu", "stale"));

		await _storage.SaveAsync(Set(id, "New Artist", "New Title"), own, TestContext.Current.CancellationToken);

		Assert.Equal([$"{id} New Artist - New Title"], TopLevel());
	}

	[Fact]
	public async Task A_new_archive_deletes_the_other_folder_stored_for_the_set()
	{
		var id = NewId();
		PlaceFolder(id, "Old Artist", "Old Title", Osz(("a.osu", "old")));
		var archive = await _storage.SaveAsync(new MemoryStream(Osz(("a.osu", "new"))),
			TestContext.Current.CancellationToken);

		await _storage.SaveAsync(Set(id, "New Artist", "New Title"), archive, TestContext.Current.CancellationToken);

		Assert.Equal([$"{id} New Artist - New Title"], TopLevel());
	}

	[Fact]
	public async Task A_set_folders_own_archive_stays_in_place_and_the_folder_is_renamed_to_the_name_of_the_set()
	{
		var id = NewId();
		var bytes = Osz(("a.osu", "x"));
		PlaceFolder(id, "Old Artist", "Old Title", bytes, ("versions/hash.osu", "made from the archive"));
		var own = await OwnArchiveAsync($"{id} Old Artist - Old Title");
		var set = Set(id, "New Artist", "New Title");

		await _storage.SaveAsync(set, own, TestContext.Current.CancellationToken);

		Assert.Equal([$"{id} New Artist - New Title"], TopLevel());
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
		Assert.Equal("made from the archive",
			await TestStreams.ReadAllAsync(await _storage.OpenAsync(set, BeatmapsetFileType.Version, "hash.osu",
				TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task A_set_folder_whose_name_differs_only_in_letter_case_is_renamed_and_keeps_its_files()
	{
		var id = NewId();
		var bytes = Osz(("a.osu", "x"));
		PlaceFolder(id, "artist", "title", bytes, ("versions/hash.osu", "made from the archive"));
		var own = await OwnArchiveAsync($"{id} artist - title");
		var set = Set(id, "Artist", "Title");

		await _storage.SaveAsync(set, own, TestContext.Current.CancellationToken);

		Assert.Equal([$"{id} Artist - Title"], TopLevel());
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
		Assert.Equal("made from the archive",
			await TestStreams.ReadAllAsync(await _storage.OpenAsync(set, BeatmapsetFileType.Version, "hash.osu",
				TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task Renaming_a_set_folder_only_in_letter_case_reports_no_name_but_the_old_and_the_new()
	{
		var id = NewId();
		PlaceFolder(id, "artist", "title", Osz(("a.osu", "x")));
		var own = await OwnArchiveAsync($"{id} artist - title");
		await using var watching = Watching.Start(_storage, _time);

		await _storage.SaveAsync(Set(id, "Artist", "Title"), own, TestContext.Current.CancellationToken);
		PlaceFolder(NewId(), "Sentinel", "Folder", Osz(("a.osu", "x")));
		var reported = await watching.ReadUntilAsync(source => source.Name.Contains("Sentinel"));

		Assert.All(reported.Where(source => !source.Name.Contains("Sentinel")),
			source => Assert.Contains(source.Name, new[] { $"{id} artist - title", $"{id} Artist - Title" }));
	}

	[Fact]
	public async Task A_set_with_a_long_title_in_a_multi_byte_script_is_stored_and_found()
	{
		var id = NewId();
		var set = Set(id, "Artist", new string('\u6f22', 100));
		var bytes = Osz(("a.osu", "x"));
		var archive = await _storage.SaveAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);

		await _storage.SaveAsync(set, archive, TestContext.Current.CancellationToken);

		var name = Assert.Single(TopLevel());
		Assert.StartsWith($"{id} Artist - ", name);
		Assert.True(Encoding.UTF8.GetByteCount(name) <= 150, "A folder name stays well within the limit of common file systems.");
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task A_folder_name_has_none_of_the_characters_a_folder_may_not_have_and_still_finds_the_set()
	{
		var id = NewId();
		var set = Set(id, "AC/DC", "What? \"Yes\" <no> | *maybe* : C:\\x...");
		var bytes = Osz(("a.osu", "x"));
		var archive = await _storage.SaveAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);

		await _storage.SaveAsync(set, archive, TestContext.Current.CancellationToken);

		var name = Assert.Single(TopLevel());
		Assert.StartsWith($"{id} ", name);
		Assert.DoesNotContain(name, character => "<>:\"/\\|?*".Contains(character) || char.IsControl(character));
		Assert.False(name.EndsWith('.') || name.EndsWith(' '));
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task A_set_with_a_very_long_artist_and_title_is_stored_and_found()
	{
		var id = NewId();
		var set = Set(id, new string('a', 300), new string('t', 300));
		var bytes = Osz(("a.osu", "x"));
		var archive = await _storage.SaveAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);

		await _storage.SaveAsync(set, archive, TestContext.Current.CancellationToken);

		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task A_packed_folder_becomes_an_archive_of_its_files_that_is_not_listed_as_a_source_of_its_own()
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
	public async Task Packing_leaves_the_loose_files_as_they_are_until_the_archive_is_taken_in()
	{
		PlaceFolder(7, "Loose", "Folder", null, ("diff1.osu", "one"), ("sb/bg.jpg", "jpg"));
		var folder = await FolderSourceAsync("7 Loose - Folder");

		await _storage.PackAsync(folder, TestContext.Current.CancellationToken);

		Assert.Equal(["diff1.osu", "sb/bg.jpg"], TempData.FilesUnder(Path.Combine(Sets, "7 Loose - Folder")));
	}

	[Fact]
	public async Task Taking_in_a_packed_archive_removes_the_loose_files_and_keeps_the_archive_as_the_sets()
	{
		PlaceFolder(7, "Loose", "Folder", null, ("diff1.osu", "one"), ("sb/bg.jpg", "jpg"));
		var folder = await FolderSourceAsync("7 Loose - Folder");
		var packed = await _storage.PackAsync(folder, TestContext.Current.CancellationToken);
		Assert.NotNull(packed);
		var set = Set(7, "Loose", "Folder");

		await _storage.SaveAsync(set, packed, TestContext.Current.CancellationToken);

		Assert.Equal(["7 Loose - Folder"], TopLevel());
		Assert.Equal(["archive.osz"], TempData.FilesUnder(Path.Combine(Sets, "7 Loose - Folder")));
		Assert.Equal(["diff1.osu", "sb/bg.jpg"],
			EntriesOf(await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true,
				TestContext.Current.CancellationToken))));
	}

	[Fact]
	public async Task A_removed_packed_archive_leaves_the_loose_files()
	{
		PlaceFolder(7, "Loose", "Folder", null, ("diff1.osu", "one"));
		var folder = await FolderSourceAsync("7 Loose - Folder");
		var packed = await _storage.PackAsync(folder, TestContext.Current.CancellationToken);
		Assert.NotNull(packed);

		await _storage.DeleteAsync(packed, TestContext.Current.CancellationToken);

		Assert.Equal(["7 Loose - Folder"], TopLevel());
		Assert.Equal(["diff1.osu"], TempData.FilesUnder(Path.Combine(Sets, "7 Loose - Folder")));
	}

	[Fact]
	public async Task A_folder_that_is_gone_packs_to_null()
	{
		PlaceFolder(7, "Loose", "Folder", null, ("diff1.osu", "one"));
		var folder = await FolderSourceAsync("7 Loose - Folder");
		Directory.Delete(Path.Combine(Sets, "7 Loose - Folder"), true);

		Assert.Null(await _storage.PackAsync(folder, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_folder_without_files_packs_to_null()
	{
		Directory.CreateDirectory(Path.Combine(Sets, "7 Empty - Folder"));
		var folder = await FolderSourceAsync("7 Empty - Folder");

		Assert.Null(await _storage.PackAsync(folder, TestContext.Current.CancellationToken));
	}

	[Theory]
	[InlineData("versions/hash.osu")]
	[InlineData("archive-novideo.osz")]
	public async Task A_set_folder_that_lost_its_archive_packs_to_null_and_stays_as_it_is(string madeFromTheArchive)
	{
		PlaceFolder(7, "Lost", "Archive", null, (madeFromTheArchive, "made from the archive"), ("diff1.osu", "one"));
		var folder = await FolderSourceAsync("7 Lost - Archive");

		Assert.Null(await _storage.PackAsync(folder, TestContext.Current.CancellationToken));
		Assert.Equal(new[] { "diff1.osu", madeFromTheArchive }.Order(StringComparer.Ordinal),
			TempData.FilesUnder(Path.Combine(Sets, "7 Lost - Archive")));
	}

	// ---- Files made from an archive ----------------------------------------------------------------------------

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

		await _storage.SaveAsync(set, type, name, TestStreams.Of("content"), TestContext.Current.CancellationToken);

		Assert.Equal("content",
			await File.ReadAllTextAsync(Path.Combine(folder, place), TestContext.Current.CancellationToken));
		Assert.True(await _storage.ExistsAsync(set, type, name, TestContext.Current.CancellationToken));
		Assert.Equal("content",
			await TestStreams.ReadAllAsync(await _storage.OpenAsync(set, type, name,
				TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task The_storyboard_is_stored_whatever_name_is_given()
	{
		var set = StoredSet(out var folder);

		await _storage.SaveAsync(set, BeatmapsetFileType.Storyboard, "", TestStreams.Of("storyboard"),
			TestContext.Current.CancellationToken);

		Assert.Equal("storyboard",
			await File.ReadAllTextAsync(Path.Combine(folder, "storyboard.osb"), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_stored_storyboard_is_found_and_opened_whatever_name_is_given()
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
	public async Task A_stored_file_is_replaced_by_one_saved_under_the_same_name()
	{
		var set = StoredSet(out _);
		await _storage.SaveAsync(set, BeatmapsetFileType.Background, "bg.jpg", TestStreams.Of("old and longer"),
			TestContext.Current.CancellationToken);

		await _storage.SaveAsync(set, BeatmapsetFileType.Background, "bg.jpg", TestStreams.Of("new"),
			TestContext.Current.CancellationToken);

		Assert.Equal("new",
			await TestStreams.ReadAllAsync(await _storage.OpenAsync(set, BeatmapsetFileType.Background, "bg.jpg",
				TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task An_opened_file_can_be_sought()
	{
		var set = StoredSet(out _);
		await _storage.SaveAsync(set, BeatmapsetFileType.Audio, "song.mp3", TestStreams.Of("content"),
			TestContext.Current.CancellationToken);

		await using var stream = await _storage.OpenAsync(set, BeatmapsetFileType.Audio, "song.mp3",
			TestContext.Current.CancellationToken);

		Assert.NotNull(stream);
		Assert.True(stream.CanSeek);
	}

	[Fact]
	public async Task A_save_that_fails_keeps_the_stored_file_and_leaves_nothing_else()
	{
		var set = StoredSet(out var folder);
		await _storage.SaveAsync(set, BeatmapsetFileType.Background, "bg.jpg", TestStreams.Of("previous"),
			TestContext.Current.CancellationToken);
		var before = TempData.FilesUnder(Sets);

		await Assert.ThrowsAsync<IOException>(() => _storage.SaveAsync(set, BeatmapsetFileType.Background, "bg.jpg",
			new TestStreams.Failing(), TestContext.Current.CancellationToken));

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
	public async Task A_name_that_is_not_a_plain_file_name_is_refused_and_stores_nothing(string name)
	{
		var set = StoredSet(out var folder);
		var before = TempData.FilesUnder(Sets);

		await Assert.ThrowsAnyAsync<ArgumentException>(() => _storage.SaveAsync(set, BeatmapsetFileType.Background,
			name, TestStreams.Of("content"), TestContext.Current.CancellationToken));

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
	public async Task A_name_that_is_not_a_plain_file_name_is_not_stored_and_reads_nothing(string name)
	{
		var set = StoredSet(out _);

		Assert.False(await _storage.ExistsAsync(set, BeatmapsetFileType.Version, name,
			TestContext.Current.CancellationToken));
		Assert.Null(await _storage.OpenAsync(set, BeatmapsetFileType.Version, name,
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_file_that_is_not_stored_is_absent_and_asking_makes_nothing()
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
	public async Task A_set_without_a_folder_has_no_files_and_no_archive()
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
	public async Task The_archive_opens_with_or_without_video_and_both_can_be_sought()
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
	public async Task The_archive_without_video_keeps_every_entry_but_the_videos_and_the_archive_stays_as_it_was()
	{
		var id = NewId();
		var bytes = Osz(("a.osu", "difficulty"), ("bg.jpg", "background"), ("sub/song.mp3", "audio"),
			("video.mp4", "video"), ("sub/clip.avi", "clip"));
		PlaceFolder(id, "Artist", "Title", bytes);
		var set = Set(id, "Artist", "Title");

		await _storage.SaveArchiveWithoutVideoAsync(set, TestContext.Current.CancellationToken);

		var made = await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, false,
			TestContext.Current.CancellationToken));
		Assert.Equal(["a.osu", "bg.jpg", "sub/song.mp3"], EntriesOf(made));
		Assert.Equal("audio", ReadEntry(made, "sub/song.mp3"));
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task The_archive_without_video_of_a_corrupt_archive_is_not_made_and_the_archive_is_kept()
	{
		var id = NewId();
		var corrupt = Encoding.UTF8.GetBytes("this is not a zip file");
		PlaceFolder(id, "Artist", "Title", corrupt);
		var set = Set(id, "Artist", "Title");

		var failure = await Record.ExceptionAsync(() =>
			_storage.SaveArchiveWithoutVideoAsync(set, TestContext.Current.CancellationToken));

		Assert.NotNull(failure);
		Assert.Null(await _storage.OpenArchiveAsync(set, false, TestContext.Current.CancellationToken));
		Assert.Equal(corrupt,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
		Assert.Equal([$"{id} Artist - Title"], TopLevel());
	}

	[Fact]
	public async Task Clearing_a_set_removes_every_file_made_from_its_archive_and_keeps_the_archive()
	{
		var id = NewId();
		var bytes = Osz(("a.osu", "x"));
		PlaceFolder(id, "Artist", "Title", bytes,
			("versions/hash.osu", "v"), ("backgrounds/bg.jpg", "b"), ("audio/song.mp3", "a"),
			("audio/preview_song.mp3", "p"),
			("video/intro.mp4", "m"), ("storyboard.osb", "s"), ("archive-novideo.osz", "n"));
		var set = Set(id, "Artist", "Title");

		await _storage.ClearAsync(set, TestContext.Current.CancellationToken);

		Assert.Equal(["archive.osz"], TempData.FilesUnder(Path.Combine(Sets, $"{id} Artist - Title")));
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
		Assert.Equal([$"{id} Artist - Title"],
			(await _storage.ListAsync(TestContext.Current.CancellationToken)).Select(source => source.Name));
	}

	[Fact]
	public async Task Clearing_a_set_without_a_folder_does_nothing()
	{
		await _storage.ClearAsync(Set(NewId(), "Nobody", "Home"), TestContext.Current.CancellationToken);

		Assert.Empty(Directory.GetFileSystemEntries(Sets));
	}

	// ---- Deleting ----------------------------------------------------------------------------------------------

	[Fact]
	public async Task A_deleted_set_loses_its_folder_with_everything_in_it_and_no_other_set_does()
	{
		var id = NewId();
		var other = NewId();
		PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "x")), ("versions/hash.osu", "v"));
		PlaceFolder(other, "Other", "Set", Osz(("a.osu", "x")));

		await _storage.DeleteAsync(Set(id, "Artist", "Title"), TestContext.Current.CancellationToken);

		Assert.Equal([$"{other} Other - Set"], TopLevel());
		Assert.Equal([$"{other} Other - Set"],
			(await _storage.ListAsync(TestContext.Current.CancellationToken)).Select(source => source.Name));
	}

	[Fact]
	public async Task Deleting_a_set_deletes_every_folder_stored_for_it()
	{
		var id = NewId();
		PlaceFolder(id, "Old", "Name", Osz(("a.osu", "x")));
		PlaceFolder(id, "New", "Name", Osz(("a.osu", "y")));

		await _storage.DeleteAsync(Set(id, "New", "Name"), TestContext.Current.CancellationToken);

		Assert.Empty(TopLevel());
	}

	[Fact]
	public async Task Deleting_a_set_leaves_no_renamed_leftover_beside_the_other_folders()
	{
		var id = NewId();
		var other = NewId();
		PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "x")));
		PlaceFolder(other, "Other", "Set", Osz(("a.osu", "x")));
		var before = Everything().Where(path => path.StartsWith($"{other} ", StringComparison.Ordinal)).ToArray();

		await _storage.DeleteAsync(Set(id, "Artist", "Title"), TestContext.Current.CancellationToken);

		Assert.Equal(before, Everything());
	}

	[Fact]
	public async Task Deleting_a_set_with_nested_files_removes_all_of_them_at_once()
	{
		var id = NewId();
		PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "x")), ("audio/preview_a.mp3", "p"), ("versions/h.osu", "v"));

		await _storage.DeleteAsync(Set(id, "Artist", "Title"), TestContext.Current.CancellationToken);

		Assert.Empty(Everything());
	}

	[Fact]
	public async Task Deleting_a_set_that_is_not_stored_does_nothing()
	{
		PlaceFolder(NewId(), "Artist", "Title", Osz(("a.osu", "x")));
		var before = Everything();

		await _storage.DeleteAsync(Set(NewId(), "Nobody", "Home"), TestContext.Current.CancellationToken);

		Assert.Equal(before, Everything());
	}

	[Fact]
	public async Task A_set_with_a_file_in_use_keeps_its_folder_and_deleting_it_fails_with_an_IO_error()
	{
		Assert.SkipUnless(OperatingSystem.IsWindows(),
			"Only Windows refuses to delete a folder with an open file in it; no portable way to make a deletion fail.");
		var id = NewId();
		var bytes = Osz(("a.osu", "x"));
		var folder = PlaceFolder(id, "Artist", "Title", bytes);
		var set = Set(id, "Artist", "Title");
		var inUse = new FileStream(Path.Combine(folder, "archive.osz"), FileMode.Open, FileAccess.Read,
			FileShare.None);

		await Assert.ThrowsAsync<IOException>(() => _storage.DeleteAsync(set, TestContext.Current.CancellationToken));
		await inUse.DisposeAsync();

		Assert.Equal([$"{id} Artist - Title"], TopLevel());
		Assert.Equal(bytes,
			await ReadAllBytesAsync(await _storage.OpenArchiveAsync(set, true, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task An_archive_placed_by_hand_is_not_removed_as_an_upload()
	{
		var path = PlaceArchive("1 Artist - Title.osz", Osz(("a.osu", "x")));
		var archive =
			Assert.IsAssignableFrom<BeatmapsetArchive>(
				Assert.Single(await _storage.ListAsync(TestContext.Current.CancellationToken)));

		await Assert.ThrowsAsync<ArgumentException>(() =>
			_storage.DeleteAsync(archive, TestContext.Current.CancellationToken));

		Assert.True(File.Exists(path));
	}

	[Fact]
	public async Task A_set_folders_own_archive_is_not_removed_as_an_upload()
	{
		var id = NewId();
		var folder = PlaceFolder(id, "Artist", "Title", Osz(("a.osu", "x")));
		var own = await OwnArchiveAsync($"{id} Artist - Title");

		await Assert.ThrowsAsync<ArgumentException>(() =>
			_storage.DeleteAsync(own, TestContext.Current.CancellationToken));

		Assert.True(File.Exists(Path.Combine(folder, "archive.osz")));
	}

	// ---- Serialising operations on a set -----------------------------------------------------------------------

	[Fact]
	public async Task A_second_operation_on_the_same_set_waits_for_the_first_to_finish()
	{
		var set = StoredSet(out _);
		var gated = new TestStreams.Gated("part ", "rest");
		var save = _storage.SaveAsync(set, BeatmapsetFileType.Background, "bg.jpg", gated,
			TestContext.Current.CancellationToken);
		await gated.Started.WaitAsync(TestContext.Current.CancellationToken);

		var clear = _storage.ClearAsync(set, TestContext.Current.CancellationToken);

		Assert.False(clear.IsCompleted);
		gated.Release();
		await save;
		await clear;
		Assert.False(await _storage.ExistsAsync(set, BeatmapsetFileType.Background, "bg.jpg",
			TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task An_operation_on_another_set_does_not_wait_for_the_first()
	{
		var set = StoredSet(out _);
		var other = StoredSet(out _);
		var gated = new TestStreams.Gated("part ", "rest");
		var save = _storage.SaveAsync(set, BeatmapsetFileType.Background, "bg.jpg", gated,
			TestContext.Current.CancellationToken);
		await gated.Started.WaitAsync(TestContext.Current.CancellationToken);

		await _storage.ClearAsync(other, TestContext.Current.CancellationToken)
			.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

		Assert.False(save.IsCompleted);
		gated.Release();
		await save;
		Assert.True(await _storage.ExistsAsync(set, BeatmapsetFileType.Background, "bg.jpg",
			TestContext.Current.CancellationToken));
	}

	// ---- Watching ----------------------------------------------------------------------------------------------

	[Fact]
	public async Task A_change_inside_a_set_folder_reports_the_folder_as_it_is_once_the_change_settled()
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
	public async Task A_new_loose_archive_is_reported_as_an_archive()
	{
		await using var watching = Watching.Start(_storage, _time);

		var bytes = Osz(("a.osu", "x"));
		PlaceArchive("9 New - Set.osz", bytes);
		var reported = await watching.ReadUntilAsync(source => source.Name == "9 New - Set.osz");

		var archive = Assert.IsAssignableFrom<BeatmapsetArchive>(reported[^1]);
		Assert.Equal(bytes.Length, archive.Stamp.Size);
	}

	[Fact]
	public async Task A_folder_that_is_gone_is_reported_as_not_existing()
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
	public async Task Watching_starts_when_the_watch_is_asked_for_not_when_it_is_first_read()
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
	public async Task Names_marked_incoming_are_never_reported()
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
	public async Task A_folder_that_vanishes_right_after_its_change_does_not_stop_the_watch()
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
	public async Task Cancelling_the_watch_ends_it()
	{
		await using var watching = Watching.Start(_storage, _time);

		var failure = await watching.CancelAndWaitAsync();

		Assert.True(failure is null or OperationCanceledException, failure?.ToString());
	}

	// ---- Helpers -----------------------------------------------------------------------------------------------

	private static int NewId() => Interlocked.Increment(ref _nextId);

	private static BeatmapsetRecord Set(int id, string artist, string title) => new()
	{
		Id = id,
		Metadata = new BeatmapsetMetadata { Artist = artist, Title = title, Creator = "Mapper" },
		Fingerprint = new ContentFingerprint(new ContentStamp(0, DateTimeOffset.UnixEpoch),
			new Md5("00000000000000000000000000000000")),
		Assets = new BeatmapsetAssets(),
		UpdatedAt = DateTimeOffset.UnixEpoch,
		CreatedAt = DateTimeOffset.UnixEpoch
	};

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

	public void Dispose() => _data.Dispose();

	/// <summary>A running watch: reads the reports, moving the fake clock past the settling time while it waits for them.</summary>
	private sealed class Watching(
		FakeTimeProvider? time,
		IAsyncEnumerable<BeatmapsetSource> reports,
		CancellationTokenSource stop) : IAsyncDisposable
	{
		private readonly IAsyncEnumerator<BeatmapsetSource> _reports = reports.GetAsyncEnumerator(stop.Token);

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

		public async ValueTask DisposeAsync()
		{
			await stop.CancelAsync();
			await _reports.DisposeAsync();
			stop.Dispose();
		}
	}
}