using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Basil.Infrastructure.Content.Storage;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Content.Tests.Storage;

/// <summary>Contract tests of FileScreenshotStorage, read through IScreenshotStorage.</summary>
public sealed class FileScreenshotStorageTests : IDisposable
{
	private readonly TempData _data = new();
	private readonly IScreenshotStorage _screenshots;

	public FileScreenshotStorageTests()
	{
		_screenshots = new FileScreenshotStorage(_data.Paths);
	}

	public void Dispose()
	{
		_data.Dispose();
	}

	[Fact]
	public async Task SavedScreenshot_OpensWithBytes()
	{
		var reference =
			await _screenshots.SaveAsync(TestStreams.Of("shot"), "png");

		Assert.Equal("shot",
			await TestStreams.ReadAllAsync(await _screenshots.OpenAsync(reference,
				TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task Screenshot_UuidWithExtension()
	{
		var reference =
			await _screenshots.SaveAsync(TestStreams.Of("shot"), "png");

		Assert.StartsWith("Screenshots/", reference.Value);
		var name = reference.Value["Screenshots/".Length..];
		Assert.Equal(".png", Path.GetExtension(name), true);
		Assert.True(Guid.TryParse(Path.GetFileNameWithoutExtension(name), out _));
	}

	[Fact]
	public async Task TwoScreenshots_DistinctReferences()
	{
		var first = await _screenshots.SaveAsync(TestStreams.Of("first"), "png");
		var second =
			await _screenshots.SaveAsync(TestStreams.Of("first"), "png");

		Assert.NotEqual(first, second);
		Assert.Equal(2, (await _screenshots.ListAsync(TestContext.Current.CancellationToken)).Count());
	}

	[Fact]
	public async Task SavedScreenshots_ReferencesListed()
	{
		var first = await _screenshots.SaveAsync(TestStreams.Of("1"), "png");
		var second = await _screenshots.SaveAsync(TestStreams.Of("2"), "jpg");

		var listed = await _screenshots.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(new[] { first, second }.Select(reference => reference.Value).Order(),
			listed.Select(reference => reference.Value).Order());
	}

	[Fact]
	public async Task NothingStored_ListsNothing()
	{
		Assert.Empty(await _screenshots.ListAsync(TestContext.Current.CancellationToken));
	}

	[Theory]
	[InlineData("p")]
	[InlineData("png")]
	[InlineData("jpeg")]
	[InlineData("12345678")]
	[InlineData("PNG")]
	public async Task ValidExtension_Accepted(string extension)
	{
		var reference =
			await _screenshots.SaveAsync(TestStreams.Of("shot"), extension);

		Assert.EndsWith("." + extension, reference.Value, StringComparison.OrdinalIgnoreCase);
		Assert.Equal("shot",
			await TestStreams.ReadAllAsync(await _screenshots.OpenAsync(reference,
				TestContext.Current.CancellationToken)));
	}

	[Theory]
	[InlineData("")]
	[InlineData("123456789")]
	[InlineData(".png")]
	[InlineData("p.ng")]
	[InlineData("p/g")]
	[InlineData("..")]
	[InlineData(" png")]
	[InlineData("png ")]
	[InlineData("pñg")]
	[InlineData("p\0g")]
	public async Task InvalidExtension_RefusedStoringNothing(string extension)
	{
		await Assert.ThrowsAnyAsync<ArgumentException>(() =>
			_screenshots.SaveAsync(TestStreams.Of("shot"), extension));

		Assert.Empty(await _screenshots.ListAsync(TestContext.Current.CancellationToken));
		Assert.Empty(TempData.FilesUnder(_data.Root));
	}

	[Fact]
	public async Task FailedSave_StoresNothing()
	{
		await Assert.ThrowsAsync<IOException>(() =>
			_screenshots.SaveAsync(new TestStreams.Failing(), "png"));

		Assert.Empty(await _screenshots.ListAsync(TestContext.Current.CancellationToken));
		Assert.Empty(TempData.FilesUnder(_data.Root));
	}

	[Fact]
	public async Task DeletedScreenshot_GoneOthersKept()
	{
		var gone = await _screenshots.SaveAsync(TestStreams.Of("gone"), "png");
		var kept = await _screenshots.SaveAsync(TestStreams.Of("kept"), "png");

		await _screenshots.DeleteAsync(gone);

		Assert.Null(await _screenshots.OpenAsync(gone, TestContext.Current.CancellationToken));
		Assert.Equal([kept.Value],
			(await _screenshots.ListAsync(TestContext.Current.CancellationToken)).Select(reference => reference.Value));
	}

	[Fact]
	public async Task UnknownScreenshotReference_NullNoDelete()
	{
		var reference = new ContentReference("Screenshots/missing.png");

		Assert.Null(await _screenshots.OpenAsync(reference, TestContext.Current.CancellationToken));
		await _screenshots.DeleteAsync(reference);
	}

	[Fact]
	public async Task ForeignReference_OpensNullOnly()
	{
		var stored = await _screenshots.SaveAsync(TestStreams.Of("kept"), "png");
		var banner = new ContentReference("Menu/Banners/shot.png");
		var address = new ContentReference(new Uri("https://example.com/Screenshots/shot.png"));
		await File.WriteAllTextAsync(Path.Combine(_data.Paths.MenuBanners.Path, "shot.png"), "banner",
			TestContext.Current.CancellationToken);

		Assert.Null(await _screenshots.OpenAsync(banner, TestContext.Current.CancellationToken));
		Assert.Null(await _screenshots.OpenAsync(address, TestContext.Current.CancellationToken));
		await _screenshots.DeleteAsync(banner);
		await _screenshots.DeleteAsync(address);

		Assert.True(File.Exists(Path.Combine(_data.Paths.MenuBanners.Path, "shot.png")));
		Assert.Equal("kept",
			await TestStreams.ReadAllAsync(await _screenshots.OpenAsync(stored,
				TestContext.Current.CancellationToken)));
	}
}