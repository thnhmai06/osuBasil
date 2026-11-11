using Basil.Application.Content.Persistence;
using Basil.Application.Content.Ports;
using Basil.Application.Content.Storage;
using Basil.Application.Servers.Capabilities;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Basil.Application.Servers.Tests.Capabilities;

/// <summary>
///     Behaviour of screenshot upload and deletion, from the XML contract of <see cref="ScopedServerScreenshots" />: an
///     upload needs <see cref="UserPrivileges.Player" />, is at most the configured number of bytes and a PNG
///     or JPEG
///     of at most the configured pixels per side, told from content; a deletion needs
///     <see cref="UserPrivileges.Moderator" />.
/// </summary>
public sealed class ScopedServerScreenshotsTests
{
	// The defaults of ServerSettings.Uploads: 10 MiB and 8192 pixels per side.
	private const int MaxBytes = 10 * 1024 * 1024;
	private const int MaxSide = 8192;
	private static readonly ContentReference Stored = new("screenshots/stored.png");

	private static MemoryStream Bytes(int length)
	{
		return new MemoryStream(new byte[length]);
	}

	private sealed class Fixture : IDisposable
	{
		public Fixture()
		{
			Inspector = (IScreenshotInspector)App.Provider.GetRequiredService(typeof(IScreenshotInspector));
			Storage = (IScreenshotStorage)App.Provider.GetRequiredService(typeof(IScreenshotStorage));
			Storage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>()).Returns(Stored);
		}

		public TestApplication App { get; } = new();
		public IScreenshotInspector Inspector { get; }
		public IScreenshotStorage Storage { get; }

		public void Dispose()
		{
			App.Dispose();
		}

		public void InspectedAs(ScreenshotInfo? info)
		{
			Inspector.InspectAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(info);
		}

		public async Task<ScopedServerScreenshots> ScreenshotsOfAsync(UserPrivileges privileges)
		{
			var user = await App.CreateUserAsync("Uploader", "password", privileges);
			return App.Server.ScopeTo(await App.LoginAsync(user)).Screenshots;
		}

		public Task<ScopedServerScreenshots> UploaderAsync()
		{
			return ScreenshotsOfAsync(UserPrivilegesExtensions.Default | UserPrivileges.Player);
		}

		public void AssertNothingStored()
		{
			Storage.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!);
		}
	}

	#region Upload: privileges

	[Fact]
	public async Task UploadNoPermission_Denied()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 100, 100));
		var screenshots = await f.ScreenshotsOfAsync(UserPrivilegesExtensions.Default & ~UserPrivileges.Player);

		var (screenshot, result) = await screenshots.UploadAsync(Bytes(10), TestContext.Current.CancellationToken);

		Assert.Null(screenshot);
		Assert.False(result.IsOk);
		Assert.Equal(UserPrivileges.Player, result.Missing);
		f.AssertNothingStored();
	}

	[Fact]
	public async Task UploadDeleteOnly_Denied()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 100, 100));
		var screenshots = await f.ScreenshotsOfAsync(
			(UserPrivilegesExtensions.Default & ~UserPrivileges.Player) | UserPrivileges.Moderator);

		var (_, result) = await screenshots.UploadAsync(Bytes(10), TestContext.Current.CancellationToken);

		Assert.Equal(UserPrivileges.Player, result.Missing);
	}

	#endregion

	#region Upload: accepted pictures

	[Theory]
	[InlineData("png")]
	[InlineData("jpg")]
	public async Task UploadAccepted_StoredByFormat(string extension)
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo(extension, 1920, 1080));
		var screenshots = await f.UploaderAsync();

		var (screenshot, result) = await screenshots.UploadAsync(Bytes(10), TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
		Assert.Equal(Stored, screenshot);
		await f.Storage.Received(1).SaveAsync(Arg.Any<Stream>(), extension);
	}

	[Fact]
	public async Task UploadAccepted_WholeStored()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 10, 10));
		var uploaded = Enumerable.Range(0, 1000).Select(i => (byte)(i % 251)).ToArray();
		byte[]? savedBytes = null;
		f.Storage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>()).Returns(call =>
		{
			var copy = new MemoryStream();
			call.Arg<Stream>().CopyTo(copy);
			savedBytes = copy.ToArray();
			return Task.FromResult(Stored);
		});
		var screenshots = await f.UploaderAsync();

		await screenshots.UploadAsync(new MemoryStream(uploaded), TestContext.Current.CancellationToken);

		Assert.Equal(uploaded, savedBytes);
	}

	[Fact]
	public async Task UploadByteCeiling_Accepted()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 100, 100));
		var screenshots = await f.UploaderAsync();

		var (_, result) = await screenshots.UploadAsync(Bytes(MaxBytes),
			TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
	}

	[Fact]
	public async Task UploadAccepted_StreamKept()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 100, 100));
		var screenshots = await f.UploaderAsync();
		var content = Bytes(10);

		await screenshots.UploadAsync(content, TestContext.Current.CancellationToken);

		Assert.True(content.CanRead);
	}

	[Theory]
	[InlineData(1, 1)]
	[InlineData(MaxSide, MaxSide)]
	public async Task UploadSides_Accepted(int width, int height)
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", width, height));
		var screenshots = await f.UploaderAsync();

		var (_, result) = await screenshots.UploadAsync(Bytes(10), TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
	}

	#endregion

	#region Upload: refusals

	[Fact]
	public async Task UploadOneOver_TooLarge()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 100, 100));
		var screenshots = await f.UploaderAsync();

		var (screenshot, result) = await screenshots.UploadAsync(
			Bytes(MaxBytes + 1), TestContext.Current.CancellationToken);

		Assert.Null(screenshot);
		Assert.Equal(ServerOutcome.TooLarge, result.Outcome);
		f.AssertNothingStored();
	}

	[Fact]
	public async Task UploadOverLimit_TooLarge()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 100, 100));
		await f.App.SetSettingsAsync(new ServerSettings
			{ Uploads = new ServerSettings.UploadSettings { ScreenshotMaxSize = 100 } });
		var screenshots = await f.UploaderAsync();

		var (_, atLimit) = await screenshots.UploadAsync(Bytes(100), TestContext.Current.CancellationToken);
		var (_, overLimit) = await screenshots.UploadAsync(Bytes(101), TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, atLimit.Outcome);
		Assert.Equal(ServerOutcome.TooLarge, overLimit.Outcome);
	}

	[Fact]
	public async Task UploadOverSide_DimensionsTooLarge()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 300, 200));
		await f.App.SetSettingsAsync(new ServerSettings
			{ Uploads = new ServerSettings.UploadSettings { ScreenshotMaxSide = 299 } });
		var screenshots = await f.UploaderAsync();

		var (_, result) = await screenshots.UploadAsync(Bytes(10), TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.DimensionsTooLarge, result.Outcome);
	}

	[Fact]
	public async Task UploadFarOver_TooLarge()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 100, 100));
		var screenshots = await f.UploaderAsync();

		var (_, result) = await screenshots.UploadAsync(
			Bytes(MaxBytes * 2), TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.TooLarge, result.Outcome);
	}

	[Fact]
	public async Task UploadUnsupported_InvalidImage()
	{
		using var f = new Fixture();
		f.InspectedAs(null);
		var screenshots = await f.UploaderAsync();

		var (screenshot, result) = await screenshots.UploadAsync(Bytes(10), TestContext.Current.CancellationToken);

		Assert.Null(screenshot);
		Assert.Equal(ServerOutcome.InvalidImage, result.Outcome);
		f.AssertNothingStored();
	}

	[Fact]
	public async Task UploadUnsupported_Quarantined()
	{
		using var f = new Fixture();
		f.InspectedAs(null);
		var screenshots = await f.UploaderAsync();

		await screenshots.UploadAsync(Bytes(10), TestContext.Current.CancellationToken);

		Assert.Single(Directory.GetFiles(Path.Combine(f.App.UploadsRoot, "Quarantine")));
		Assert.Empty(Directory.GetFiles(Path.Combine(f.App.UploadsRoot, "Spool")));
	}

	[Fact]
	public async Task UploadAccepted_NothingLeftBehind()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 100, 100));
		var screenshots = await f.UploaderAsync();

		await screenshots.UploadAsync(Bytes(10), TestContext.Current.CancellationToken);

		Assert.Empty(Directory.GetFiles(Path.Combine(f.App.UploadsRoot, "Spool")));
		Assert.False(Directory.Exists(Path.Combine(f.App.UploadsRoot, "Quarantine")) &&
		             Directory.GetFiles(Path.Combine(f.App.UploadsRoot, "Quarantine")).Length > 0);
	}

	[Fact]
	public async Task UploadEmpty_NotPicture()
	{
		using var f = new Fixture();
		f.InspectedAs(null);
		var screenshots = await f.UploaderAsync();

		var (_, result) = await screenshots.UploadAsync(Bytes(0), TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.InvalidImage, result.Outcome);
		f.AssertNothingStored();
	}

	[Theory]
	[InlineData(0, 100)]
	[InlineData(100, 0)]
	[InlineData(-1, 100)]
	[InlineData(100, -1)]
	public async Task NonPositiveSide_InvalidImage(int width, int height)
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", width, height));
		var screenshots = await f.UploaderAsync();

		var (_, result) = await screenshots.UploadAsync(Bytes(10), TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.InvalidImage, result.Outcome);
		f.AssertNothingStored();
	}

	[Theory]
	[InlineData(MaxSide + 1, 100)]
	[InlineData(100, MaxSide + 1)]
	[InlineData(MaxSide + 1, MaxSide + 1)]
	public async Task UploadPixelCeiling_DimensionsTooLarge(
		int width, int height)
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", width, height));
		var screenshots = await f.UploaderAsync();

		var (screenshot, result) = await screenshots.UploadAsync(Bytes(10), TestContext.Current.CancellationToken);

		Assert.Null(screenshot);
		Assert.Equal(ServerOutcome.DimensionsTooLarge, result.Outcome);
		f.AssertNothingStored();
	}

	[Fact]
	public async Task UploadOversized_NotInspected()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 100, 100));
		var screenshots = await f.UploaderAsync();

		await screenshots.UploadAsync(Bytes(MaxBytes + 1),
			TestContext.Current.CancellationToken);

		await f.Inspector.DidNotReceiveWithAnyArgs().InspectAsync(default!, TestContext.Current.CancellationToken);
	}

	#endregion

	#region Delete

	[Fact]
	public async Task DeleteWithPermission_Removed()
	{
		using var f = new Fixture();
		var screenshots = await f.ScreenshotsOfAsync(UserPrivilegesExtensions.Default | UserPrivileges.Moderator);

		var result = await screenshots.DeleteAsync(Stored);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
		await f.Storage.Received(1).DeleteAsync(Stored);
	}

	[Fact]
	public async Task DeleteNoPermission_Denied()
	{
		using var f = new Fixture();
		var screenshots = await f.ScreenshotsOfAsync(UserPrivilegesExtensions.Default & ~UserPrivileges.Moderator);

		var result = await screenshots.DeleteAsync(Stored);

		Assert.False(result.IsOk);
		Assert.Equal(UserPrivileges.Moderator, result.Missing);
		await f.Storage.DidNotReceiveWithAnyArgs().DeleteAsync(default);
	}

	[Fact]
	public async Task DeleteUploadOnly_Denied()
	{
		using var f = new Fixture();
		var screenshots = await f.ScreenshotsOfAsync(
			(UserPrivilegesExtensions.Default & ~UserPrivileges.Moderator) | UserPrivileges.Player);

		var result = await screenshots.DeleteAsync(Stored);

		Assert.Equal(UserPrivileges.Moderator, result.Missing);
	}

	#endregion
}