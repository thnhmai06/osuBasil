using Basil.Application.Persistence.Content;
using Basil.Domain.Content;
using Basil.Infrastructure.Persistence.Content;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Content;

/// <summary>Contract tests of FileMenuIconStorage, read through IMenuIconStorage.</summary>
public sealed class FileMenuIconStorageTests : IDisposable
{
	private readonly TempData _data = new();
	private readonly IMenuIconStorage _icon;

	public FileMenuIconStorageTests()
	{
		_icon = new FileMenuIconStorage(_data.Paths);
	}

	[Fact]
	public async Task Nothing_is_stored_at_first()
	{
		Assert.Null(await StoredAsync());
	}

	[Fact]
	public async Task The_saved_icon_opens_with_its_reference_and_the_bytes_it_was_saved_with()
	{
		var reference =
			await _icon.SaveAsync("icon.png", TestStreams.Of("icon"), TestContext.Current.CancellationToken);

		var stored = await StoredAsync();

		Assert.NotNull(stored);
		Assert.Equal(reference, stored.Value.Reference);
		Assert.Equal("icon", stored.Value.Text);
	}

	[Fact]
	public async Task Saving_an_icon_under_the_same_name_replaces_it()
	{
		await _icon.SaveAsync("icon.png", TestStreams.Of("old and longer"), TestContext.Current.CancellationToken);

		await _icon.SaveAsync("icon.png", TestStreams.Of("new"), TestContext.Current.CancellationToken);

		Assert.Equal("new", (await StoredAsync())?.Text);
	}

	[Fact]
	public async Task Saving_an_icon_under_another_name_replaces_any_stored_icon()
	{
		await _icon.SaveAsync("old.png", TestStreams.Of("old"), TestContext.Current.CancellationToken);

		var reference = await _icon.SaveAsync("new.jpg", TestStreams.Of("new"), TestContext.Current.CancellationToken);

		var stored = await StoredAsync();
		Assert.NotNull(stored);
		Assert.Equal(reference, stored.Value.Reference);
		Assert.Equal("new", stored.Value.Text);
		Assert.Equal(["new.jpg"], TempData.FilesUnder(_data.Paths.MenuIcon.Path));
	}

	[Fact]
	public async Task A_save_that_fails_keeps_the_stored_icon()
	{
		await _icon.SaveAsync("old.png", TestStreams.Of("previous"), TestContext.Current.CancellationToken);

		await Assert.ThrowsAsync<IOException>(() =>
			_icon.SaveAsync("new.jpg", new TestStreams.Failing(), TestContext.Current.CancellationToken));

		Assert.Equal("previous", (await StoredAsync())?.Text);
	}

	[Theory]
	[MemberData(nameof(EscapingNames))]
	[MemberData(nameof(DeviceNames))]
	[MemberData(nameof(NotCanonicalNames))]
	public async Task Saving_under_a_name_that_is_not_safe_is_refused_and_keeps_the_stored_icon(string name)
	{
		await _icon.SaveAsync("old.png", TestStreams.Of("previous"), TestContext.Current.CancellationToken);

		await Assert.ThrowsAnyAsync<ArgumentException>(() =>
			_icon.SaveAsync(name, TestStreams.Of("new"), TestContext.Current.CancellationToken));

		Assert.Equal(["old.png"], TempData.FilesUnder(_data.Paths.MenuIcon.Path));
		Assert.Equal("previous", (await StoredAsync())?.Text);
	}

	[Fact]
	public async Task A_deleted_icon_is_no_longer_stored()
	{
		await _icon.SaveAsync("icon.png", TestStreams.Of("icon"), TestContext.Current.CancellationToken);

		await _icon.DeleteAsync(TestContext.Current.CancellationToken);

		Assert.Null(await StoredAsync());
	}

	[Fact]
	public async Task Deleting_when_no_icon_is_stored_does_nothing()
	{
		await _icon.DeleteAsync(TestContext.Current.CancellationToken);

		Assert.Null(await StoredAsync());
	}

	/// <summary>Opens the stored icon, reads it to its end and closes it.</summary>
	private async Task<(ContentReference Reference, string Text)?> StoredAsync()
	{
		var opened = await _icon.OpenAsync(TestContext.Current.CancellationToken);
		if (opened is null) return null;

		return (opened.Value.Reference, await TestStreams.ReadAllAsync(opened.Value.Content));
	}

	public static TheoryData<string> EscapingNames => UnsafeNames.Escaping;

	public static TheoryData<string> DeviceNames => UnsafeNames.Devices;

	public static TheoryData<string> NotCanonicalNames => UnsafeNames.NotCanonical;

	public void Dispose() => _data.Dispose();
}