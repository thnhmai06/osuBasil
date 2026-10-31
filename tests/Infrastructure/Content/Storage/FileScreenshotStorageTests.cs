using Basil.Application.Content.Persistence;
using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Basil.Infrastructure.Content.Persistence;
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
	public async Task A_saved_screenshot_opens_with_the_bytes_it_was_saved_with()
	{
		var reference =
			await _screenshots.SaveAsync(TestStreams.Of("shot"), "png", TestContext.Current.CancellationToken);

		Assert.Equal("shot",
			await TestStreams.ReadAllAsync(await _screenshots.OpenAsync(reference,
				TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task A_screenshot_is_stored_under_a_uuid_name_with_the_extension()
	{
		var reference =
			await _screenshots.SaveAsync(TestStreams.Of("shot"), "png", TestContext.Current.CancellationToken);

		Assert.StartsWith("Screenshots/", reference.Value);
		var name = reference.Value["Screenshots/".Length..];
		Assert.Equal(".png", Path.GetExtension(name), true);
		Assert.True(Guid.TryParse(Path.GetFileNameWithoutExtension(name), out _));
	}

	[Fact]
	public async Task Two_screenshots_get_different_references_and_neither_replaces_the_other()
	{
		var first = await _screenshots.SaveAsync(TestStreams.Of("first"), "png", TestContext.Current.CancellationToken);
		var second =
			await _screenshots.SaveAsync(TestStreams.Of("first"), "png", TestContext.Current.CancellationToken);

		Assert.NotEqual(first, second);
		Assert.Equal(2, (await _screenshots.ListAsync(TestContext.Current.CancellationToken)).Count());
	}

	[Fact]
	public async Task The_references_of_the_saved_screenshots_are_listed()
	{
		var first = await _screenshots.SaveAsync(TestStreams.Of("1"), "png", TestContext.Current.CancellationToken);
		var second = await _screenshots.SaveAsync(TestStreams.Of("2"), "jpg", TestContext.Current.CancellationToken);

		var listed = await _screenshots.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(new[] { first, second }.Select(reference => reference.Value).Order(),
			listed.Select(reference => reference.Value).Order());
	}

	[Fact]
	public async Task Nothing_is_listed_when_nothing_is_stored()
	{
		Assert.Empty(await _screenshots.ListAsync(TestContext.Current.CancellationToken));
	}

	[Theory]
	[InlineData("p")]
	[InlineData("png")]
	[InlineData("jpeg")]
	[InlineData("12345678")]
	[InlineData("PNG")]
	public async Task An_extension_of_one_to_eight_ascii_letters_or_digits_is_accepted(string extension)
	{
		var reference =
			await _screenshots.SaveAsync(TestStreams.Of("shot"), extension, TestContext.Current.CancellationToken);

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
	public async Task Any_other_extension_is_refused_and_stores_nothing(string extension)
	{
		await Assert.ThrowsAnyAsync<ArgumentException>(() =>
			_screenshots.SaveAsync(TestStreams.Of("shot"), extension, TestContext.Current.CancellationToken));

		Assert.Empty(await _screenshots.ListAsync(TestContext.Current.CancellationToken));
		Assert.Empty(TempData.FilesUnder(_data.Root));
	}

	[Fact]
	public async Task A_save_that_fails_stores_nothing()
	{
		await Assert.ThrowsAsync<IOException>(() =>
			_screenshots.SaveAsync(new TestStreams.Failing(), "png", TestContext.Current.CancellationToken));

		Assert.Empty(await _screenshots.ListAsync(TestContext.Current.CancellationToken));
		Assert.Empty(TempData.FilesUnder(_data.Root));
	}

	[Fact]
	public async Task A_deleted_screenshot_is_no_longer_stored_and_the_others_are()
	{
		var gone = await _screenshots.SaveAsync(TestStreams.Of("gone"), "png", TestContext.Current.CancellationToken);
		var kept = await _screenshots.SaveAsync(TestStreams.Of("kept"), "png", TestContext.Current.CancellationToken);

		await _screenshots.DeleteAsync(gone, TestContext.Current.CancellationToken);

		Assert.Null(await _screenshots.OpenAsync(gone, TestContext.Current.CancellationToken));
		Assert.Equal([kept.Value],
			(await _screenshots.ListAsync(TestContext.Current.CancellationToken)).Select(reference => reference.Value));
	}

	[Fact]
	public async Task A_reference_to_no_stored_screenshot_opens_as_null_and_deletes_nothing()
	{
		var reference = new ContentReference("Screenshots/missing.png");

		Assert.Null(await _screenshots.OpenAsync(reference, TestContext.Current.CancellationToken));
		await _screenshots.DeleteAsync(reference, TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task A_reference_to_another_storage_or_an_address_opens_as_null_and_deletes_nothing()
	{
		var stored = await _screenshots.SaveAsync(TestStreams.Of("kept"), "png", TestContext.Current.CancellationToken);
		var banner = new ContentReference("Menu/Banners/shot.png");
		var address = new ContentReference(new Uri("https://example.com/Screenshots/shot.png"));
		await File.WriteAllTextAsync(Path.Combine(_data.Paths.MenuBanners.Path, "shot.png"), "banner",
			TestContext.Current.CancellationToken);

		Assert.Null(await _screenshots.OpenAsync(banner, TestContext.Current.CancellationToken));
		Assert.Null(await _screenshots.OpenAsync(address, TestContext.Current.CancellationToken));
		await _screenshots.DeleteAsync(banner, TestContext.Current.CancellationToken);
		await _screenshots.DeleteAsync(address, TestContext.Current.CancellationToken);

		Assert.True(File.Exists(Path.Combine(_data.Paths.MenuBanners.Path, "shot.png")));
		Assert.Equal("kept",
			await TestStreams.ReadAllAsync(await _screenshots.OpenAsync(stored,
				TestContext.Current.CancellationToken)));
	}
}