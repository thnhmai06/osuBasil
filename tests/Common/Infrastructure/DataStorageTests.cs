using Basil.Domain.Content;
using Basil.Common.Infrastructure.Files;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Common.Infrastructure.Tests;

/// <summary>Contract tests of DataStorage: the files of one directory, saved atomically and never outside it.</summary>
public sealed class DataStorageTests : IDisposable
{
	private readonly TempData _data = new();

	private DataStorage Storage => _data.Paths.Replays;

	public void Dispose()
	{
		_data.Dispose();
	}

	[Theory]
	[InlineData("..\\escape.txt")]
	[InlineData("../escape.txt")]
	[InlineData("folder/../../escape.txt")]
	[InlineData("/rooted.txt")]
	[InlineData("")]
	[InlineData("  ")]
	public void Resolve_refuses_a_name_that_is_not_safe(string name)
	{
		Assert.ThrowsAny<ArgumentException>(() => Storage.Resolve(name));
	}

	[Fact]
	public async Task A_file_placed_by_hand_under_a_name_no_reference_can_refer_to_is_not_listed()
	{
		await Storage.SaveAsync("fine.txt", TestStreams.Of("kept"), TestContext.Current.CancellationToken);
		// Only a path in the extended form lets Windows keep a name that is a device name.
		var odd = OperatingSystem.IsWindows()
			? @"\\?\" + Path.Combine(Storage.Path, "NUL.txt")
			: Path.Combine(Storage.Path, "NUL.txt");
		await File.WriteAllTextAsync(odd, "placed by hand", TestContext.Current.CancellationToken);

		Assert.Equal(["fine.txt"], Storage.Keys);
		Assert.Single(Storage);
		Assert.Equal(["fine.txt"], Storage.Select(pair => pair.Key));
	}

	[Fact]
	public void Resolve_places_a_name_inside_the_directory()
	{
		Assert.Equal(Path.Combine(Storage.Path, "folder", "file.txt"), Storage.Resolve("folder/file.txt"));
	}

	[Fact]
	public async Task SaveAsync_stores_a_new_file_and_creates_its_folders()
	{
		await Storage.SaveAsync("folder/deeper/file.txt", TestStreams.Of("hello"),
			TestContext.Current.CancellationToken);

		Assert.Equal("hello", await TestStreams.ReadAllAsync(Storage.Open("folder/deeper/file.txt")));
	}

	[Fact]
	public async Task SaveAsync_replaces_the_file_already_there()
	{
		await Storage.SaveAsync("file.txt", TestStreams.Of("old content that is longer"),
			TestContext.Current.CancellationToken);

		await Storage.SaveAsync("file.txt", TestStreams.Of("new"), TestContext.Current.CancellationToken);

		Assert.Equal("new", await TestStreams.ReadAllAsync(Storage.Open("file.txt")));
	}

	[Fact]
	public async Task SaveAsync_that_fails_keeps_the_previous_file_and_leaves_nothing_else()
	{
		await Storage.SaveAsync("file.txt", TestStreams.Of("previous"), TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<IOException>(() =>
			Storage.SaveAsync("file.txt", new TestStreams.Failing(), TestContext.Current.CancellationToken));

		Assert.Equal("previous", await TestStreams.ReadAllAsync(Storage.Open("file.txt")));
		Assert.Equal(["file.txt"], TempData.FilesUnder(Storage.Path));
	}

	[Fact]
	public async Task SaveAsync_that_fails_for_a_new_file_stores_nothing()
	{
		await Assert.ThrowsAsync<IOException>(() =>
			Storage.SaveAsync("file.txt", new TestStreams.Failing(), TestContext.Current.CancellationToken));

		Assert.Empty(TempData.FilesUnder(Storage.Path));
		Assert.False(Storage.ContainsKey("file.txt"));
	}

	[Fact]
	public async Task SaveAsync_refuses_an_unsafe_name_and_stores_nothing()
	{
		await Assert.ThrowsAnyAsync<ArgumentException>(() =>
			Storage.SaveAsync("../escape.txt", TestStreams.Of("data"), TestContext.Current.CancellationToken));

		Assert.Empty(TempData.FilesUnder(_data.Root));
	}

	[Fact]
	public async Task A_reader_sees_the_previous_file_until_the_save_completes()
	{
		await Storage.SaveAsync("file.txt", TestStreams.Of("previous"), TestContext.Current.CancellationToken);
		var gated = new TestStreams.Gated("new ", "content");

		var save = Storage.SaveAsync("file.txt", gated, TestContext.Current.CancellationToken);
		await gated.Started.WaitAsync(TestContext.Current.CancellationToken);
		var during = await TestStreams.ReadAllAsync(Storage.Open("file.txt"));
		gated.Release();
		await save;

		Assert.Equal("previous", during);
		Assert.Equal("new content", await TestStreams.ReadAllAsync(Storage.Open("file.txt")));
	}

	[Fact]
	public async Task A_save_in_progress_writes_to_a_temporary_file_named_after_none_of_the_name()
	{
		var gated = new TestStreams.Gated("part ", "rest");

		var save = Storage.SaveAsync("secret-name.txt", gated, TestContext.Current.CancellationToken);
		await gated.Started.WaitAsync(TestContext.Current.CancellationToken);
		var during = Directory.GetFiles(Storage.Path).Select(file => Path.GetFileName(file)).ToList();
		var listedDuring = Storage.Keys.ToList();
		gated.Release();
		await save;

		var temporary = Assert.Single(during);
		Assert.DoesNotContain("secret", temporary, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("name", temporary, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("txt", temporary, StringComparison.OrdinalIgnoreCase);
		Assert.Empty(listedDuring);
		Assert.Equal(["secret-name.txt"], TempData.FilesUnder(Storage.Path));
	}

	[Fact]
	public void Temporary_names_of_one_target_differ_and_lie_in_the_directory_of_the_target()
	{
		var target = Storage.Resolve("folder/target.txt");

		var first = TempFiles.Name(target);
		var second = TempFiles.Name(target);

		Assert.NotEqual(first, second);
		Assert.Equal(Path.GetDirectoryName(target), Path.GetDirectoryName(first));
	}

	[Fact]
	public async Task Keys_lists_the_stored_files_by_their_names_with_slashes_between_folders()
	{
		await Storage.SaveAsync("a.txt", TestStreams.Of("1"), TestContext.Current.CancellationToken);
		await Storage.SaveAsync("folder/b.txt", TestStreams.Of("2"), TestContext.Current.CancellationToken);

		Assert.Equal(["a.txt", "folder/b.txt"], Storage.Keys.Order(StringComparer.Ordinal));
		Assert.Equal(2, Storage.Count);
		Assert.Equal(["a.txt", "folder/b.txt"],
			Storage.Select(pair => pair.Key).Order(StringComparer.Ordinal));
	}

	[Fact]
	public void Reading_a_directory_that_does_not_exist_finds_no_files()
	{
		var missing = new DataStorage(_data.Root, "Missing");

		Assert.Empty(missing.Keys);
		Assert.Empty(missing.Search("*"));
		Assert.Null(missing.Open("file.txt"));
	}

	[Fact]
	public async Task A_stored_file_is_found_by_name_and_an_unsafe_or_missing_name_is_not()
	{
		await Storage.SaveAsync("file.txt", TestStreams.Of("12345"), TestContext.Current.CancellationToken);

		Assert.True(Storage.ContainsKey("file.txt"));
		Assert.Equal(5, Storage["file.txt"].Length);
		Assert.True(Storage.TryGetValue("file.txt", out var found));
		Assert.Equal(5, found.Length);
		Assert.False(Storage.ContainsKey("missing.txt"));
		Assert.False(Storage.ContainsKey("../file.txt"));
		Assert.False(Storage.TryGetValue("../file.txt", out _));
		Assert.Throws<KeyNotFoundException>(() => Storage["missing.txt"]);
	}

	[Fact]
	public async Task Open_gives_a_read_only_stream_of_the_file()
	{
		await Storage.SaveAsync("file.txt", TestStreams.Of("content"), TestContext.Current.CancellationToken);

		await using var stream = Storage.Open("file.txt");

		Assert.NotNull(stream);
		Assert.True(stream.CanRead);
		Assert.False(stream.CanWrite);
	}

	[Theory]
	[InlineData("missing.txt")]
	[InlineData("../file.txt")]
	[InlineData("")]
	public async Task Open_gives_null_for_a_missing_or_unsafe_name(string name)
	{
		await File.WriteAllTextAsync(Path.Combine(_data.Root, "file.txt"), "outside",
			TestContext.Current.CancellationToken);

		Assert.Null(Storage.Open(name));
	}

	[Fact]
	public async Task Move_renames_a_file_and_keeps_its_content()
	{
		await Storage.SaveAsync("from.txt", TestStreams.Of("data"), TestContext.Current.CancellationToken);

		Assert.True(Storage.Move("from.txt", "folder/to.txt"));

		Assert.False(Storage.ContainsKey("from.txt"));
		Assert.Equal("data", await TestStreams.ReadAllAsync(Storage.Open("folder/to.txt")));
	}

	[Fact]
	public void Move_of_a_missing_file_does_nothing()
	{
		Assert.False(Storage.Move("missing.txt", "to.txt"));
		Assert.Empty(Storage.Keys);
	}

	[Fact]
	public async Task Move_onto_a_taken_name_changes_neither_file()
	{
		await Storage.SaveAsync("from.txt", TestStreams.Of("from"), TestContext.Current.CancellationToken);
		await Storage.SaveAsync("to.txt", TestStreams.Of("to"), TestContext.Current.CancellationToken);

		Assert.False(Storage.Move("from.txt", "to.txt"));

		Assert.Equal("from", await TestStreams.ReadAllAsync(Storage.Open("from.txt")));
		Assert.Equal("to", await TestStreams.ReadAllAsync(Storage.Open("to.txt")));
	}

	[Fact]
	public async Task Move_refuses_an_unsafe_new_name_and_keeps_the_file()
	{
		await Storage.SaveAsync("from.txt", TestStreams.Of("data"), TestContext.Current.CancellationToken);

		Assert.Throws<ArgumentException>(() => Storage.Move("from.txt", "../escape.txt"));

		Assert.True(Storage.ContainsKey("from.txt"));
		Assert.False(File.Exists(Path.Combine(_data.Root, "escape.txt")));
	}

	[Fact]
	public async Task Delete_removes_the_file_and_only_that_file()
	{
		await Storage.SaveAsync("gone.txt", TestStreams.Of("1"), TestContext.Current.CancellationToken);
		await Storage.SaveAsync("kept.txt", TestStreams.Of("2"), TestContext.Current.CancellationToken);

		Storage.Delete("gone.txt");

		Assert.Equal(["kept.txt"], Storage.Keys);
	}

	[Theory]
	[InlineData("missing.txt")]
	[InlineData("../outside.txt")]
	public async Task Delete_of_a_missing_or_unsafe_name_does_nothing(string name)
	{
		await File.WriteAllTextAsync(Path.Combine(_data.Root, "outside.txt"), "outside",
			TestContext.Current.CancellationToken);

		Storage.Delete(name);

		Assert.True(File.Exists(Path.Combine(_data.Root, "outside.txt")));
	}

	[Fact]
	public async Task DeleteAllExcept_keeps_only_the_named_file()
	{
		await Storage.SaveAsync("keep.txt", TestStreams.Of("1"), TestContext.Current.CancellationToken);
		await Storage.SaveAsync("other.txt", TestStreams.Of("2"), TestContext.Current.CancellationToken);
		await Storage.SaveAsync("folder/nested.txt", TestStreams.Of("3"), TestContext.Current.CancellationToken);

		Storage.DeleteAllExcept("keep.txt");

		Assert.Equal(["keep.txt"], Storage.Keys);
	}

	[Fact]
	public async Task DeleteAllExcept_nothing_deletes_every_file()
	{
		await Storage.SaveAsync("a.txt", TestStreams.Of("1"), TestContext.Current.CancellationToken);
		await Storage.SaveAsync("folder/b.txt", TestStreams.Of("2"), TestContext.Current.CancellationToken);

		Storage.DeleteAllExcept(null);

		Assert.Empty(Storage.Keys);
	}

	[Fact]
	public async Task Search_lists_the_names_of_the_files_directly_in_the_directory_that_match()
	{
		await Storage.SaveAsync("a.txt", TestStreams.Of("1"), TestContext.Current.CancellationToken);
		await Storage.SaveAsync("b.log", TestStreams.Of("2"), TestContext.Current.CancellationToken);
		await Storage.SaveAsync("folder/c.txt", TestStreams.Of("3"), TestContext.Current.CancellationToken);

		Assert.Equal(["a.txt"], Storage.Search("*.txt"));
	}

	[Fact]
	public void A_reference_to_a_file_names_the_directory_and_gives_the_name_back()
	{
		var storage = _data.Paths.MenuBanners;

		var reference = storage.ReferenceOf("folder/file.png");

		Assert.Equal("Menu/Banners/folder/file.png", reference.Value);
		Assert.True(storage.TryGetName(reference, out var name));
		Assert.Equal("folder/file.png", name);
	}

	[Fact]
	public void A_reference_to_another_directory_or_to_an_address_names_no_file()
	{
		var storage = _data.Paths.MenuBanners;

		Assert.False(storage.TryGetName(new ContentReference("Menu/Seasonals/file.png"), out _));
		Assert.False(storage.TryGetName(new ContentReference("Menu/Banners"), out _));
		Assert.False(storage.TryGetName(new ContentReference(new Uri("https://example.com/Menu/Banners/file.png")),
			out _));
		Assert.False(storage.TryGetName(default, out _));
	}
}