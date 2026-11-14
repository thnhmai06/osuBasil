using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Basil.Infrastructure.Content.Storage;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Content.Tests.Storage;

/// <summary>Contract tests of FileMenuSeasonalsStorage, read through IMenuSeasonalsStorage.</summary>
public sealed class FileMenuSeasonalsStorageTests : IDisposable
{
	private readonly TempData _data = new();
	private readonly IMenuSeasonalsStorage _seasonals;

	public FileMenuSeasonalsStorageTests()
	{
		_seasonals = new FileMenuSeasonalsStorage(_data.Paths);
	}

	public static TheoryData<string> EscapingNames => UnsafeNames.Escaping;

	public static TheoryData<string> DeviceNames => UnsafeNames.Devices;

	public static TheoryData<string> NotCanonicalNames => UnsafeNames.NotCanonical;

	public void Dispose()
	{
		_data.Dispose();
	}

	[Fact]
	public async Task SavedBackground_OpensWithBytes()
	{
		var reference = await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("autumn"));

		Assert.Equal("autumn",
			await TestStreams.ReadAllAsync(await _seasonals.OpenAsync(reference,
				TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task SavedBackground_ListedWithOthers()
	{
		var autumn =
			await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("1"));
		var spring =
			await _seasonals.SaveAsync("spring.jpg", TestStreams.Of("2"));

		var listed = await _seasonals.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(new[] { autumn, spring }.Select(reference => reference.Value).Order(),
			listed.Select(reference => reference.Value).Order());
	}

	[Fact]
	public async Task NothingStored_ListsNothing()
	{
		Assert.Empty(await _seasonals.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task ExistingName_ReplacesOnlyBackground()
	{
		var autumn = await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("old and longer"));
		var spring = await _seasonals.SaveAsync("spring.jpg", TestStreams.Of("spring"));

		await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("new"));

		Assert.Equal("new",
			await TestStreams.ReadAllAsync(await _seasonals.OpenAsync(autumn, TestContext.Current.CancellationToken)));
		Assert.Equal("spring",
			await TestStreams.ReadAllAsync(await _seasonals.OpenAsync(spring, TestContext.Current.CancellationToken)));
		Assert.Equal(2, (await _seasonals.ListAsync(TestContext.Current.CancellationToken)).Count());
	}

	[Fact]
	public async Task FailedSave_KeepsStoredBackground()
	{
		var reference = await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("previous"));

		await Assert.ThrowsAsync<IOException>(() =>
			_seasonals.SaveAsync("autumn.jpg", new TestStreams.Failing()));

		Assert.Equal("previous",
			await TestStreams.ReadAllAsync(await _seasonals.OpenAsync(reference,
				TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task DeletedBackground_GoneOthersKept()
	{
		var autumn =
			await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("1"));
		var spring =
			await _seasonals.SaveAsync("spring.jpg", TestStreams.Of("2"));

		await _seasonals.DeleteAsync(autumn);

		Assert.Null(await _seasonals.OpenAsync(autumn, TestContext.Current.CancellationToken));
		Assert.Equal([spring.Value],
			(await _seasonals.ListAsync(TestContext.Current.CancellationToken)).Select(reference => reference.Value));
	}

	[Fact]
	public async Task UnknownBackgroundReference_NullNoDelete()
	{
		var reference = new ContentReference("Menu/Seasonals/missing.jpg");

		Assert.Null(await _seasonals.OpenAsync(reference, TestContext.Current.CancellationToken));
		await _seasonals.DeleteAsync(reference);
	}

	[Fact]
	public async Task ForeignReference_OpensNullOnly()
	{
		var stored =
			await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("kept"));
		var banner = new ContentReference("Menu/Banners/autumn.jpg");
		var address = new ContentReference(new Uri("https://example.com/Menu/Seasonals/autumn.jpg"));
		await File.WriteAllTextAsync(Path.Combine(_data.Paths.MenuBanners.Path, "autumn.jpg"), "banner",
			TestContext.Current.CancellationToken);

		Assert.Null(await _seasonals.OpenAsync(banner, TestContext.Current.CancellationToken));
		Assert.Null(await _seasonals.OpenAsync(address, TestContext.Current.CancellationToken));
		await _seasonals.DeleteAsync(banner);
		await _seasonals.DeleteAsync(address);

		Assert.True(File.Exists(Path.Combine(_data.Paths.MenuBanners.Path, "autumn.jpg")));
		Assert.Equal("kept",
			await TestStreams.ReadAllAsync(await _seasonals.OpenAsync(stored, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task RenamedBackground_NewReferenceOnly()
	{
		var autumn = await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("autumn"));

		var renamed = await _seasonals.RenameAsync(autumn, "fall.jpg");

		Assert.NotNull(renamed);
		Assert.Equal("Menu/Seasonals/fall.jpg", renamed.Value.Value);
		Assert.Equal("autumn",
			await TestStreams.ReadAllAsync(await _seasonals.OpenAsync(renamed.Value,
				TestContext.Current.CancellationToken)));
		Assert.Null(await _seasonals.OpenAsync(autumn, TestContext.Current.CancellationToken));
		Assert.Equal([renamed.Value.Value],
			(await _seasonals.ListAsync(TestContext.Current.CancellationToken)).Select(reference => reference.Value));
	}

	[Fact]
	public async Task UnknownRenameReference_GivesNull()
	{
		var renamed = await _seasonals.RenameAsync(new ContentReference("Menu/Seasonals/missing.jpg"), "fall.jpg");

		Assert.Null(renamed);
		Assert.Empty(await _seasonals.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task TakenRenameName_NullAndUnchanged()
	{
		var autumn = await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("autumn"));
		var spring = await _seasonals.SaveAsync("spring.jpg", TestStreams.Of("spring"));

		var renamed = await _seasonals.RenameAsync(autumn, "spring.jpg");

		Assert.Null(renamed);
		Assert.Equal("autumn",
			await TestStreams.ReadAllAsync(await _seasonals.OpenAsync(autumn, TestContext.Current.CancellationToken)));
		Assert.Equal("spring",
			await TestStreams.ReadAllAsync(await _seasonals.OpenAsync(spring, TestContext.Current.CancellationToken)));
	}

	[Theory]
	[MemberData(nameof(EscapingNames))]
	[MemberData(nameof(DeviceNames))]
	[MemberData(nameof(NotCanonicalNames))]
	public async Task UnsafeRenameName_RefusedKeepingBackground(string newName)
	{
		var autumn = await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("autumn"));

		await Assert.ThrowsAnyAsync<ArgumentException>(() =>
			_seasonals.RenameAsync(autumn, newName));

		Assert.Equal("autumn",
			await TestStreams.ReadAllAsync(await _seasonals.OpenAsync(autumn, TestContext.Current.CancellationToken)));
		Assert.Equal(["autumn.jpg"], TempData.FilesUnder(_data.Paths.MenuSeasonals.Path));
	}

	[Theory]
	[MemberData(nameof(EscapingNames))]
	[MemberData(nameof(DeviceNames))]
	[MemberData(nameof(NotCanonicalNames))]
	public async Task UnsafeSaveName_RefusedStoringNothing(string name)
	{
		await Assert.ThrowsAnyAsync<ArgumentException>(() =>
			_seasonals.SaveAsync(name, TestStreams.Of("data")));

		Assert.Empty(await _seasonals.ListAsync(TestContext.Current.CancellationToken));
		Assert.Empty(TempData.FilesUnder(_data.Root));
	}
}