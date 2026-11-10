using Basil.Domain.Content;
using Basil.Infrastructure.Common.Storage;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Common.Tests.Storage;

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
	public void UnsafeName_RefusedByResolve(string name)
	{
		Assert.ThrowsAny<ArgumentException>(() => Storage.Resolve(name));
	}

	[Fact]
	public async Task UnreferencableFile_NotListed()
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
	public void PlainName_ResolvedInsideDirectory()
	{
		Assert.Equal(Path.Combine(Storage.Path, "folder", "file.txt"), Storage.Resolve("folder/file.txt"));
	}

	[Fact]
	public async Task NewFile_SavedWithFolders()
	{
		await Storage.SaveAsync("folder/deeper/file.txt", TestStreams.Of("hello"),
			TestContext.Current.CancellationToken);

		Assert.Equal("hello", await TestStreams.ReadAllAsync(Storage.Open("folder/deeper/file.txt")));
	}

	[Fact]
	public async Task ExistingFile_Replaced()
	{
		await Storage.SaveAsync("file.txt", TestStreams.Of("old content that is longer"),
			TestContext.Current.CancellationToken);

		await Storage.SaveAsync("file.txt", TestStreams.Of("new"), TestContext.Current.CancellationToken);

		Assert.Equal("new", await TestStreams.ReadAllAsync(Storage.Open("file.txt")));
	}

	[Fact]
	public async Task FailedSave_KeepsPreviousFile()
	{
		await Storage.SaveAsync("file.txt", TestStreams.Of("previous"), TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<IOException>(() =>
			Storage.SaveAsync("file.txt", new TestStreams.Failing(), TestContext.Current.CancellationToken));

		Assert.Equal("previous", await TestStreams.ReadAllAsync(Storage.Open("file.txt")));
		Assert.Equal(["file.txt"], TempData.FilesUnder(Storage.Path));
	}

	[Fact]
	public async Task FailedNewSave_StoresNothing()
	{
		await Assert.ThrowsAsync<IOException>(() =>
			Storage.SaveAsync("file.txt", new TestStreams.Failing(), TestContext.Current.CancellationToken));

		Assert.Empty(TempData.FilesUnder(Storage.Path));
		Assert.False(Storage.ContainsKey("file.txt"));
	}

	[Fact]
	public async Task UnsafeName_RefusedStoringNothing()
	{
		await Assert.ThrowsAnyAsync<ArgumentException>(() =>
			Storage.SaveAsync("../escape.txt", TestStreams.Of("data"), TestContext.Current.CancellationToken));

		Assert.Empty(TempData.FilesUnder(_data.Root));
	}

	[Fact]
	public async Task SaveInProgress_PreviousFileVisible()
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
	public async Task SaveInProgress_WritesTemporaryFile()
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
	public void TemporaryNames_UniquePerTarget()
	{
		var target = Storage.Resolve("folder/target.txt");

		var first = TempFiles.Name(target);
		var second = TempFiles.Name(target);

		Assert.NotEqual(first, second);
		Assert.Equal(Path.GetDirectoryName(target), Path.GetDirectoryName(first));
	}

	[Fact]
	public async Task Keys_ListStoredPaths()
	{
		await Storage.SaveAsync("a.txt", TestStreams.Of("1"), TestContext.Current.CancellationToken);
		await Storage.SaveAsync("folder/b.txt", TestStreams.Of("2"), TestContext.Current.CancellationToken);

		Assert.Equal(["a.txt", "folder/b.txt"], Storage.Keys.Order(StringComparer.Ordinal));
		Assert.Equal(2, Storage.Count);
		Assert.Equal(["a.txt", "folder/b.txt"],
			Storage.Select(pair => pair.Key).Order(StringComparer.Ordinal));
	}

	[Fact]
	public void MissingDirectory_ReadsNoFiles()
	{
		var missing = new DataStorage(_data.Root, "Missing");

		Assert.Empty(missing.Keys);
		Assert.Empty(missing.Search("*"));
		Assert.Null(missing.Open("file.txt"));
	}

	[Fact]
	public async Task KnownOrUnsafe_OnlyKnownFound()
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
	public async Task Open_ReadOnlyStream()
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
	public async Task MissingOrUnsafe_OpenGivesNull(string name)
	{
		await File.WriteAllTextAsync(Path.Combine(_data.Root, "file.txt"), "outside",
			TestContext.Current.CancellationToken);

		Assert.Null(Storage.Open(name));
	}

	[Fact]
	public async Task Move_RenamesKeepingContent()
	{
		await Storage.SaveAsync("from.txt", TestStreams.Of("data"), TestContext.Current.CancellationToken);

		Assert.True(Storage.Move("from.txt", "folder/to.txt"));

		Assert.False(Storage.ContainsKey("from.txt"));
		Assert.Equal("data", await TestStreams.ReadAllAsync(Storage.Open("folder/to.txt")));
	}

	[Fact]
	public void MissingFileMove_DoesNothing()
	{
		Assert.False(Storage.Move("missing.txt", "to.txt"));
		Assert.Empty(Storage.Keys);
	}

	[Fact]
	public async Task TakenNameMove_ChangesNeitherFile()
	{
		await Storage.SaveAsync("from.txt", TestStreams.Of("from"), TestContext.Current.CancellationToken);
		await Storage.SaveAsync("to.txt", TestStreams.Of("to"), TestContext.Current.CancellationToken);

		Assert.False(Storage.Move("from.txt", "to.txt"));

		Assert.Equal("from", await TestStreams.ReadAllAsync(Storage.Open("from.txt")));
		Assert.Equal("to", await TestStreams.ReadAllAsync(Storage.Open("to.txt")));
	}

	[Fact]
	public async Task UnsafeMoveName_RefusedKeepingFile()
	{
		await Storage.SaveAsync("from.txt", TestStreams.Of("data"), TestContext.Current.CancellationToken);

		Assert.Throws<ArgumentException>(() => Storage.Move("from.txt", "../escape.txt"));

		Assert.True(Storage.ContainsKey("from.txt"));
		Assert.False(File.Exists(Path.Combine(_data.Root, "escape.txt")));
	}

	[Fact]
	public async Task Delete_RemovesOnlyFile()
	{
		await Storage.SaveAsync("gone.txt", TestStreams.Of("1"), TestContext.Current.CancellationToken);
		await Storage.SaveAsync("kept.txt", TestStreams.Of("2"), TestContext.Current.CancellationToken);

		Storage.Delete("gone.txt");

		Assert.Equal(["kept.txt"], Storage.Keys);
	}

	[Theory]
	[InlineData("missing.txt")]
	[InlineData("../outside.txt")]
	public async Task MissingOrUnsafe_DeleteDoesNothing(string name)
	{
		await File.WriteAllTextAsync(Path.Combine(_data.Root, "outside.txt"), "outside",
			TestContext.Current.CancellationToken);

		Storage.Delete(name);

		Assert.True(File.Exists(Path.Combine(_data.Root, "outside.txt")));
	}

	[Fact]
	public async Task DeleteAllExcept_KeepsNamedFile()
	{
		await Storage.SaveAsync("keep.txt", TestStreams.Of("1"), TestContext.Current.CancellationToken);
		await Storage.SaveAsync("other.txt", TestStreams.Of("2"), TestContext.Current.CancellationToken);
		await Storage.SaveAsync("folder/nested.txt", TestStreams.Of("3"), TestContext.Current.CancellationToken);

		Storage.DeleteAllExcept("keep.txt");

		Assert.Equal(["keep.txt"], Storage.Keys);
	}

	[Fact]
	public async Task EmptyKeepSet_DeletesEveryFile()
	{
		await Storage.SaveAsync("a.txt", TestStreams.Of("1"), TestContext.Current.CancellationToken);
		await Storage.SaveAsync("folder/b.txt", TestStreams.Of("2"), TestContext.Current.CancellationToken);

		Storage.DeleteAllExcept(null);

		Assert.Empty(Storage.Keys);
	}

	[Fact]
	public async Task Search_ListsMatchingNames()
	{
		await Storage.SaveAsync("a.txt", TestStreams.Of("1"), TestContext.Current.CancellationToken);
		await Storage.SaveAsync("b.log", TestStreams.Of("2"), TestContext.Current.CancellationToken);
		await Storage.SaveAsync("folder/c.txt", TestStreams.Of("3"), TestContext.Current.CancellationToken);

		Assert.Equal(["a.txt"], Storage.Search("*.txt"));
	}

	[Fact]
	public void FileReference_DirectoryAndName()
	{
		var storage = _data.Paths.MenuBanners;

		var reference = storage.ReferenceOf("folder/file.png");

		Assert.Equal("Menu/Banners/folder/file.png", reference.Value);
		Assert.True(storage.TryGetName(reference, out var name));
		Assert.Equal("folder/file.png", name);
	}

	[Fact]
	public void ForeignOrAddress_FindsNoFile()
	{
		var storage = _data.Paths.MenuBanners;

		Assert.False(storage.TryGetName(new ContentReference("Menu/Seasonals/file.png"), out _));
		Assert.False(storage.TryGetName(new ContentReference("Menu/Banners"), out _));
		Assert.False(storage.TryGetName(new ContentReference(new Uri("https://example.com/Menu/Banners/file.png")),
			out _));
		Assert.False(storage.TryGetName(default, out _));
	}
}