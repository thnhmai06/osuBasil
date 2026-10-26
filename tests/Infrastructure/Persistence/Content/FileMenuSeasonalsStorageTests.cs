using Basil.Application.Persistence.Content;
using Basil.Domain.Content;
using Basil.Infrastructure.Persistence.Content;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Content;

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
	public async Task A_saved_background_opens_with_the_bytes_it_was_saved_with()
	{
		var reference = await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("autumn"),
			TestContext.Current.CancellationToken);

		Assert.Equal("autumn",
			await TestStreams.ReadAllAsync(await _seasonals.OpenAsync(reference,
				TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task The_reference_of_a_saved_background_is_listed_with_those_of_the_others()
	{
		var autumn =
			await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("1"), TestContext.Current.CancellationToken);
		var spring =
			await _seasonals.SaveAsync("spring.jpg", TestStreams.Of("2"), TestContext.Current.CancellationToken);

		var listed = await _seasonals.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(new[] { autumn, spring }.Select(reference => reference.Value).Order(),
			listed.Select(reference => reference.Value).Order());
	}

	[Fact]
	public async Task Nothing_is_listed_when_nothing_is_stored()
	{
		Assert.Empty(await _seasonals.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Saving_under_a_name_in_use_replaces_that_background_only()
	{
		var autumn = await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("old and longer"),
			TestContext.Current.CancellationToken);
		var spring = await _seasonals.SaveAsync("spring.jpg", TestStreams.Of("spring"),
			TestContext.Current.CancellationToken);

		await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("new"), TestContext.Current.CancellationToken);

		Assert.Equal("new",
			await TestStreams.ReadAllAsync(await _seasonals.OpenAsync(autumn, TestContext.Current.CancellationToken)));
		Assert.Equal("spring",
			await TestStreams.ReadAllAsync(await _seasonals.OpenAsync(spring, TestContext.Current.CancellationToken)));
		Assert.Equal(2, (await _seasonals.ListAsync(TestContext.Current.CancellationToken)).Count());
	}

	[Fact]
	public async Task A_save_that_fails_keeps_the_background_stored_under_the_name()
	{
		var reference = await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("previous"),
			TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<IOException>(() =>
			_seasonals.SaveAsync("autumn.jpg", new TestStreams.Failing(), TestContext.Current.CancellationToken));

		Assert.Equal("previous",
			await TestStreams.ReadAllAsync(await _seasonals.OpenAsync(reference,
				TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task A_deleted_background_is_no_longer_stored_and_the_others_are()
	{
		var autumn =
			await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("1"), TestContext.Current.CancellationToken);
		var spring =
			await _seasonals.SaveAsync("spring.jpg", TestStreams.Of("2"), TestContext.Current.CancellationToken);

		await _seasonals.DeleteAsync(autumn, TestContext.Current.CancellationToken);

		Assert.Null(await _seasonals.OpenAsync(autumn, TestContext.Current.CancellationToken));
		Assert.Equal([spring.Value],
			(await _seasonals.ListAsync(TestContext.Current.CancellationToken)).Select(reference => reference.Value));
	}

	[Fact]
	public async Task A_reference_to_no_stored_background_opens_as_null_and_deletes_nothing()
	{
		var reference = new ContentReference("Menu/Seasonals/missing.jpg");

		Assert.Null(await _seasonals.OpenAsync(reference, TestContext.Current.CancellationToken));
		await _seasonals.DeleteAsync(reference, TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task A_reference_to_another_storage_or_an_address_opens_as_null_and_deletes_nothing()
	{
		var stored =
			await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("kept"), TestContext.Current.CancellationToken);
		var banner = new ContentReference("Menu/Banners/autumn.jpg");
		var address = new ContentReference(new Uri("https://example.com/Menu/Seasonals/autumn.jpg"));
		await File.WriteAllTextAsync(Path.Combine(_data.Paths.MenuBanners.Path, "autumn.jpg"), "banner",
			TestContext.Current.CancellationToken);

		Assert.Null(await _seasonals.OpenAsync(banner, TestContext.Current.CancellationToken));
		Assert.Null(await _seasonals.OpenAsync(address, TestContext.Current.CancellationToken));
		await _seasonals.DeleteAsync(banner, TestContext.Current.CancellationToken);
		await _seasonals.DeleteAsync(address, TestContext.Current.CancellationToken);

		Assert.True(File.Exists(Path.Combine(_data.Paths.MenuBanners.Path, "autumn.jpg")));
		Assert.Equal("kept",
			await TestStreams.ReadAllAsync(await _seasonals.OpenAsync(stored, TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task A_renamed_background_opens_under_its_new_reference_with_its_bytes_and_no_longer_under_the_old()
	{
		var autumn = await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("autumn"),
			TestContext.Current.CancellationToken);

		var renamed = await _seasonals.RenameAsync(autumn, "fall.jpg", TestContext.Current.CancellationToken);

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
	public async Task Renaming_a_reference_to_no_stored_background_gives_null()
	{
		var renamed = await _seasonals.RenameAsync(new ContentReference("Menu/Seasonals/missing.jpg"), "fall.jpg",
			TestContext.Current.CancellationToken);

		Assert.Null(renamed);
		Assert.Empty(await _seasonals.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Renaming_onto_a_taken_name_gives_null_and_changes_neither_background()
	{
		var autumn = await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("autumn"),
			TestContext.Current.CancellationToken);
		var spring = await _seasonals.SaveAsync("spring.jpg", TestStreams.Of("spring"),
			TestContext.Current.CancellationToken);

		var renamed = await _seasonals.RenameAsync(autumn, "spring.jpg", TestContext.Current.CancellationToken);

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
	public async Task Renaming_to_a_name_that_is_not_safe_is_refused_and_keeps_the_background(string newName)
	{
		var autumn = await _seasonals.SaveAsync("autumn.jpg", TestStreams.Of("autumn"),
			TestContext.Current.CancellationToken);

		await Assert.ThrowsAnyAsync<ArgumentException>(() =>
			_seasonals.RenameAsync(autumn, newName, TestContext.Current.CancellationToken));

		Assert.Equal("autumn",
			await TestStreams.ReadAllAsync(await _seasonals.OpenAsync(autumn, TestContext.Current.CancellationToken)));
		Assert.Equal(["autumn.jpg"], TempData.FilesUnder(_data.Paths.MenuSeasonals.Path));
	}

	[Theory]
	[MemberData(nameof(EscapingNames))]
	[MemberData(nameof(DeviceNames))]
	[MemberData(nameof(NotCanonicalNames))]
	public async Task Saving_under_a_name_that_is_not_safe_is_refused_and_stores_nothing(string name)
	{
		await Assert.ThrowsAnyAsync<ArgumentException>(() =>
			_seasonals.SaveAsync(name, TestStreams.Of("data"), TestContext.Current.CancellationToken));

		Assert.Empty(await _seasonals.ListAsync(TestContext.Current.CancellationToken));
		Assert.Empty(TempData.FilesUnder(_data.Root));
	}
}