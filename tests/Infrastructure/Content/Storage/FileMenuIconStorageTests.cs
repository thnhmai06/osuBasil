using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Basil.Infrastructure.Content.Storage;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Content.Tests.Storage;

/// <summary>Contract tests of FileMenuIconStorage, read through IMenuIconStorage.</summary>
public sealed class FileMenuIconStorageTests : IDisposable
{
	private readonly TempData _data = new();
	private readonly IMenuIconStorage _icon;

	public FileMenuIconStorageTests()
	{
		_icon = new FileMenuIconStorage(_data.Paths);
	}

	public static TheoryData<string> EscapingNames => UnsafeNames.Escaping;

	public static TheoryData<string> DeviceNames => UnsafeNames.Devices;

	public static TheoryData<string> NotCanonicalNames => UnsafeNames.NotCanonical;

	public void Dispose()
	{
		_data.Dispose();
	}

	[Fact]
	public async Task FreshStorage_StoresNothing()
	{
		Assert.Null(await StoredAsync());
	}

	[Fact]
	public async Task SavedIcon_ReferenceAndBytes()
	{
		var reference =
			await _icon.SaveAsync("icon.png", TestStreams.Of("icon"));

		var stored = await StoredAsync();

		Assert.NotNull(stored);
		Assert.Equal(reference, stored.Value.Reference);
		Assert.Equal("icon", stored.Value.Text);
	}

	[Fact]
	public async Task SameIconName_Replaced()
	{
		await _icon.SaveAsync("icon.png", TestStreams.Of("old and longer"));

		await _icon.SaveAsync("icon.png", TestStreams.Of("new"));

		Assert.Equal("new", (await StoredAsync())?.Text);
	}

	[Fact]
	public async Task NewIconName_ReplacesStoredIcon()
	{
		await _icon.SaveAsync("old.png", TestStreams.Of("old"));

		var reference = await _icon.SaveAsync("new.jpg", TestStreams.Of("new"));

		var stored = await StoredAsync();
		Assert.NotNull(stored);
		Assert.Equal(reference, stored.Value.Reference);
		Assert.Equal("new", stored.Value.Text);
		Assert.Equal(["new.jpg"], TempData.FilesUnder(_data.Paths.MenuIcon.Path));
	}

	[Fact]
	public async Task FailedSave_KeepsStoredIcon()
	{
		await _icon.SaveAsync("old.png", TestStreams.Of("previous"));

		await Assert.ThrowsAsync<IOException>(() =>
			_icon.SaveAsync("new.jpg", new TestStreams.Failing()));

		Assert.Equal("previous", (await StoredAsync())?.Text);
	}

	[Theory]
	[MemberData(nameof(EscapingNames))]
	[MemberData(nameof(DeviceNames))]
	[MemberData(nameof(NotCanonicalNames))]
	public async Task UnsafeSaveName_RefusedKeepingIcon(string name)
	{
		await _icon.SaveAsync("old.png", TestStreams.Of("previous"));

		await Assert.ThrowsAnyAsync<ArgumentException>(() =>
			_icon.SaveAsync(name, TestStreams.Of("new")));

		Assert.Equal(["old.png"], TempData.FilesUnder(_data.Paths.MenuIcon.Path));
		Assert.Equal("previous", (await StoredAsync())?.Text);
	}

	[Fact]
	public async Task DeletedIcon_NoLongerStored()
	{
		await _icon.SaveAsync("icon.png", TestStreams.Of("icon"));

		await _icon.DeleteAsync();

		Assert.Null(await StoredAsync());
	}

	[Fact]
	public async Task NoStoredIcon_DeleteDoesNothing()
	{
		await _icon.DeleteAsync();

		Assert.Null(await StoredAsync());
	}

	/// <summary>Opens the stored icon, reads it to its end and closes it.</summary>
	private async Task<(ContentReference Reference, string Text)?> StoredAsync()
	{
		var opened = await _icon.OpenAsync(TestContext.Current.CancellationToken);
		if (opened is null) return null;

		return (opened.Value.Reference, await TestStreams.ReadAllAsync(opened.Value.Content));
	}
}