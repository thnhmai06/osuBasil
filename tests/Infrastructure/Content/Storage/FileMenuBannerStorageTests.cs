using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Basil.Infrastructure.Content.Storage;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Content.Tests.Storage;

/// <summary>Contract tests of FileMenuBannerStorage, read through IMenuBannerStorage.</summary>
public sealed class FileMenuBannerStorageTests : IDisposable
{
	private readonly IMenuBannerStorage _banners;
	private readonly TempData _data = new();

	public FileMenuBannerStorageTests()
	{
		_banners = new FileMenuBannerStorage(_data.Paths);
	}

	public static TheoryData<string> EscapingNames => UnsafeNames.Escaping;

	public static TheoryData<string> DeviceNames => UnsafeNames.Devices;

	public static TheoryData<string> NotCanonicalNames => UnsafeNames.NotCanonical;

	public void Dispose()
	{
		_data.Dispose();
	}

	[Fact]
	public async Task SavedImage_OpensWithBytes()
	{
		var reference = await _banners.SaveAsync("summer.png", TestStreams.Of("summer"));

		Assert.Equal("summer",
			await TestStreams.ReadAllAsync(await _banners.OpenAsync(reference, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task SavedImage_ListedWithOthers()
	{
		var summer = await _banners.SaveAsync("summer.png", TestStreams.Of("1"));
		var winter = await _banners.SaveAsync("winter.png", TestStreams.Of("2"));

		var listed = await _banners.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(new[] { summer, winter }.Select(reference => reference.Value).Order(),
			listed.Select(reference => reference.Value).Order());
	}

	[Fact]
	public async Task NothingStored_ListsNothing()
	{
		Assert.Empty(await _banners.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task ExistingName_ReplacesOnlyImage()
	{
		var summer = await _banners.SaveAsync("summer.png", TestStreams.Of("old and longer"));
		var winter = await _banners.SaveAsync("winter.png", TestStreams.Of("winter"));

		var replaced =
			await _banners.SaveAsync("summer.png", TestStreams.Of("new"));

		Assert.Equal(summer, replaced);
		Assert.Equal("new",
			await TestStreams.ReadAllAsync(await _banners.OpenAsync(summer, TestContext.Current.CancellationToken)));
		Assert.Equal("winter",
			await TestStreams.ReadAllAsync(await _banners.OpenAsync(winter, TestContext.Current.CancellationToken)));
		Assert.Equal(2, (await _banners.ListAsync(TestContext.Current.CancellationToken)).Count());
	}

	[Fact]
	public async Task FailedSave_KeepsStoredImage()
	{
		var reference = await _banners.SaveAsync("summer.png", TestStreams.Of("previous"));

		await Assert.ThrowsAsync<IOException>(() =>
			_banners.SaveAsync("summer.png", new TestStreams.Failing()));

		Assert.Equal("previous",
			await TestStreams.ReadAllAsync(await _banners.OpenAsync(reference, TestContext.Current.CancellationToken)));
		Assert.Single(await _banners.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task DeletedImage_GoneOthersKept()
	{
		var summer = await _banners.SaveAsync("summer.png", TestStreams.Of("1"));
		var winter = await _banners.SaveAsync("winter.png", TestStreams.Of("2"));

		await _banners.DeleteAsync(summer);

		Assert.Null(await _banners.OpenAsync(summer, TestContext.Current.CancellationToken));
		Assert.Equal([winter.Value],
			(await _banners.ListAsync(TestContext.Current.CancellationToken)).Select(reference => reference.Value));
	}

	[Fact]
	public async Task UnknownImageReference_OpensNull()
	{
		var reference = new ContentReference("Menu/Banners/missing.png");

		Assert.Null(await _banners.OpenAsync(reference, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task ForeignReference_OpensNullOnly()
	{
		var stored =
			await _banners.SaveAsync("summer.png", TestStreams.Of("kept"));
		var seasonal = new ContentReference("Menu/Seasonals/summer.png");
		var address = new ContentReference(new Uri("https://example.com/Menu/Banners/summer.png"));
		await File.WriteAllTextAsync(Path.Combine(_data.Paths.MenuSeasonals.Path, "summer.png"), "seasonal",
			TestContext.Current.CancellationToken);

		Assert.Null(await _banners.OpenAsync(seasonal, TestContext.Current.CancellationToken));
		Assert.Null(await _banners.OpenAsync(address, TestContext.Current.CancellationToken));
		Assert.Null(await _banners.OpenAsync(default, TestContext.Current.CancellationToken));
		await _banners.DeleteAsync(seasonal);
		await _banners.DeleteAsync(address);
		await _banners.DeleteAsync(default);

		Assert.True(File.Exists(Path.Combine(_data.Paths.MenuSeasonals.Path, "summer.png")));
		Assert.Equal("kept",
			await TestStreams.ReadAllAsync(await _banners.OpenAsync(stored, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task MissingImageDelete_DoesNothing()
	{
		await _banners.DeleteAsync(new ContentReference("Menu/Banners/missing.png"));

		Assert.Empty(await _banners.ListAsync(TestContext.Current.CancellationToken));
	}

	[Theory]
	[MemberData(nameof(EscapingNames))]
	[MemberData(nameof(DeviceNames))]
	[MemberData(nameof(NotCanonicalNames))]
	public async Task UnsafeSaveName_RefusedStoringNothing(string name)
	{
		await Assert.ThrowsAnyAsync<ArgumentException>(() =>
			_banners.SaveAsync(name, TestStreams.Of("data")));

		Assert.Empty(await _banners.ListAsync(TestContext.Current.CancellationToken));
		Assert.Empty(TempData.FilesUnder(_data.Root));
	}

	[Fact]
	public async Task
		Saving_under_a_name_with_a_trailing_dot_is_refused_without_replacing_the_image_Windows_resolves_it_to()
	{
		var reference =
			await _banners.SaveAsync("image.png", TestStreams.Of("old"));

		await Assert.ThrowsAnyAsync<ArgumentException>(() =>
			_banners.SaveAsync("image.png.", TestStreams.Of("new")));

		Assert.Equal("old",
			await TestStreams.ReadAllAsync(await _banners.OpenAsync(reference, TestContext.Current.CancellationToken)));
	}
}