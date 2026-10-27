using Basil.Application.Content.Persistence;
using Basil.Domain.Content;
using Basil.Infrastructure.Content.Persistence;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Content.Tests.Persistence;

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
	public async Task A_saved_image_opens_with_the_bytes_it_was_saved_with()
	{
		var reference = await _banners.SaveAsync("summer.png", TestStreams.Of("summer"),
			TestContext.Current.CancellationToken);

		Assert.Equal("summer",
			await TestStreams.ReadAllAsync(await _banners.OpenAsync(reference, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task The_reference_of_a_saved_image_is_listed_with_those_of_the_others()
	{
		var summer = await _banners.SaveAsync("summer.png", TestStreams.Of("1"), TestContext.Current.CancellationToken);
		var winter = await _banners.SaveAsync("winter.png", TestStreams.Of("2"), TestContext.Current.CancellationToken);

		var listed = await _banners.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(new[] { summer, winter }.Select(reference => reference.Value).Order(),
			listed.Select(reference => reference.Value).Order());
	}

	[Fact]
	public async Task Nothing_is_listed_when_nothing_is_stored()
	{
		Assert.Empty(await _banners.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Saving_under_a_name_in_use_replaces_that_image_only()
	{
		var summer = await _banners.SaveAsync("summer.png", TestStreams.Of("old and longer"),
			TestContext.Current.CancellationToken);
		var winter = await _banners.SaveAsync("winter.png", TestStreams.Of("winter"),
			TestContext.Current.CancellationToken);

		var replaced =
			await _banners.SaveAsync("summer.png", TestStreams.Of("new"), TestContext.Current.CancellationToken);

		Assert.Equal(summer, replaced);
		Assert.Equal("new",
			await TestStreams.ReadAllAsync(await _banners.OpenAsync(summer, TestContext.Current.CancellationToken)));
		Assert.Equal("winter",
			await TestStreams.ReadAllAsync(await _banners.OpenAsync(winter, TestContext.Current.CancellationToken)));
		Assert.Equal(2, (await _banners.ListAsync(TestContext.Current.CancellationToken)).Count());
	}

	[Fact]
	public async Task A_save_that_fails_keeps_the_image_stored_under_the_name()
	{
		var reference = await _banners.SaveAsync("summer.png", TestStreams.Of("previous"),
			TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<IOException>(() =>
			_banners.SaveAsync("summer.png", new TestStreams.Failing(), TestContext.Current.CancellationToken));

		Assert.Equal("previous",
			await TestStreams.ReadAllAsync(await _banners.OpenAsync(reference, TestContext.Current.CancellationToken)));
		Assert.Single(await _banners.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_deleted_image_is_no_longer_stored_and_the_others_are()
	{
		var summer = await _banners.SaveAsync("summer.png", TestStreams.Of("1"), TestContext.Current.CancellationToken);
		var winter = await _banners.SaveAsync("winter.png", TestStreams.Of("2"), TestContext.Current.CancellationToken);

		await _banners.DeleteAsync(summer, TestContext.Current.CancellationToken);

		Assert.Null(await _banners.OpenAsync(summer, TestContext.Current.CancellationToken));
		Assert.Equal([winter.Value],
			(await _banners.ListAsync(TestContext.Current.CancellationToken)).Select(reference => reference.Value));
	}

	[Fact]
	public async Task A_reference_to_no_stored_image_opens_as_null()
	{
		var reference = new ContentReference("Menu/Banners/missing.png");

		Assert.Null(await _banners.OpenAsync(reference, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_reference_to_another_storage_or_an_address_opens_as_null_and_deletes_nothing()
	{
		var stored =
			await _banners.SaveAsync("summer.png", TestStreams.Of("kept"), TestContext.Current.CancellationToken);
		var seasonal = new ContentReference("Menu/Seasonals/summer.png");
		var address = new ContentReference(new Uri("https://example.com/Menu/Banners/summer.png"));
		await File.WriteAllTextAsync(Path.Combine(_data.Paths.MenuSeasonals.Path, "summer.png"), "seasonal",
			TestContext.Current.CancellationToken);

		Assert.Null(await _banners.OpenAsync(seasonal, TestContext.Current.CancellationToken));
		Assert.Null(await _banners.OpenAsync(address, TestContext.Current.CancellationToken));
		Assert.Null(await _banners.OpenAsync(default, TestContext.Current.CancellationToken));
		await _banners.DeleteAsync(seasonal, TestContext.Current.CancellationToken);
		await _banners.DeleteAsync(address, TestContext.Current.CancellationToken);
		await _banners.DeleteAsync(default, TestContext.Current.CancellationToken);

		Assert.True(File.Exists(Path.Combine(_data.Paths.MenuSeasonals.Path, "summer.png")));
		Assert.Equal("kept",
			await TestStreams.ReadAllAsync(await _banners.OpenAsync(stored, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task Deleting_an_image_that_is_not_stored_does_nothing()
	{
		await _banners.DeleteAsync(new ContentReference("Menu/Banners/missing.png"),
			TestContext.Current.CancellationToken);

		Assert.Empty(await _banners.ListAsync(TestContext.Current.CancellationToken));
	}

	[Theory]
	[MemberData(nameof(EscapingNames))]
	[MemberData(nameof(DeviceNames))]
	[MemberData(nameof(NotCanonicalNames))]
	public async Task Saving_under_a_name_that_is_not_safe_is_refused_and_stores_nothing(string name)
	{
		await Assert.ThrowsAnyAsync<ArgumentException>(() =>
			_banners.SaveAsync(name, TestStreams.Of("data"), TestContext.Current.CancellationToken));

		Assert.Empty(await _banners.ListAsync(TestContext.Current.CancellationToken));
		Assert.Empty(TempData.FilesUnder(_data.Root));
	}

	[Fact]
	public async Task
		Saving_under_a_name_with_a_trailing_dot_is_refused_without_replacing_the_image_Windows_resolves_it_to()
	{
		var reference =
			await _banners.SaveAsync("image.png", TestStreams.Of("old"), TestContext.Current.CancellationToken);

		await Assert.ThrowsAnyAsync<ArgumentException>(() =>
			_banners.SaveAsync("image.png.", TestStreams.Of("new"), TestContext.Current.CancellationToken));

		Assert.Equal("old",
			await TestStreams.ReadAllAsync(await _banners.OpenAsync(reference, TestContext.Current.CancellationToken)));
	}
}