using Basil.Application.Content.Capabilities;
using Basil.Application.Content.Persistence;
using Basil.Application.Servers.Runtime;
using Basil.Domain.Content;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Basil.Application.Servers.Tests.Runtime;

/// <summary>
///     Behaviour of screenshot upload and deletion, from the XML contract of <see cref="ScopedServerScreenshots" />: an
///     upload needs <see cref="UserPermissions.ScreenshotUpload" />, is at most 4 MiB and a PNG or JPEG of at most
///     8192 by 8192 pixels, told from content; a deletion needs <see cref="UserPermissions.ScreenshotDelete" />.
/// </summary>
public sealed class ScopedServerScreenshotsTests
{
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
			Storage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Stored);
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

		public async Task<ScopedServerScreenshots> ScreenshotsOfAsync(UserPermissions permissions)
		{
			var user = await App.CreateUserAsync("Uploader", "password", permissions);
			return App.Server.ScopeTo(await App.LoginAsync(user)).Screenshots;
		}

		public Task<ScopedServerScreenshots> UploaderAsync()
		{
			return ScreenshotsOfAsync(UserPermissionsGroups.Default | UserPermissions.ScreenshotUpload);
		}

		public void AssertNothingStored()
		{
			Storage.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!);
		}
	}

	#region Upload: permissions

	[Fact]
	public async Task UploadAsync_WithoutScreenshotUpload_IsDeniedNamingItAndStoresNothing()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 100, 100));
		var screenshots = await f.ScreenshotsOfAsync(UserPermissionsGroups.Default & ~UserPermissions.ScreenshotUpload);

		var (screenshot, result) = await screenshots.UploadAsync(Bytes(10), TestContext.Current.CancellationToken);

		Assert.Null(screenshot);
		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.ScreenshotUpload, result.Missing);
		f.AssertNothingStored();
	}

	[Fact]
	public async Task UploadAsync_HoldingOnlyScreenshotDelete_IsStillDenied()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 100, 100));
		var screenshots = await f.ScreenshotsOfAsync(
			(UserPermissionsGroups.Default & ~UserPermissions.ScreenshotUpload) | UserPermissions.ScreenshotDelete);

		var (_, result) = await screenshots.UploadAsync(Bytes(10), TestContext.Current.CancellationToken);

		Assert.Equal(UserPermissions.ScreenshotUpload, result.Missing);
	}

	[Fact]
	public async Task UploadAsync_ScreenshotUploadSuspendedByRestriction_IsDenied()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 100, 100));
		var user = await f.App.CreateUserAsync("Restricted", "password",
			UserPermissionsGroups.Default | UserPermissions.ScreenshotUpload);
		f.App.AddRestriction(user, UserPermissions.ScreenshotUpload);
		var screenshots = f.App.Server.ScopeTo(await f.App.LoginAsync(user)).Screenshots;

		var (_, result) = await screenshots.UploadAsync(Bytes(10), TestContext.Current.CancellationToken);

		Assert.Equal(UserPermissions.ScreenshotUpload, result.Missing);
		f.AssertNothingStored();
	}

	#endregion

	#region Upload: accepted pictures

	[Theory]
	[InlineData("png")]
	[InlineData("jpg")]
	public async Task UploadAsync_AcceptedPicture_IsStoredUnderItsFormatAndTheReferenceReturned(string extension)
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo(extension, 1920, 1080));
		var screenshots = await f.UploaderAsync();

		var (screenshot, result) = await screenshots.UploadAsync(Bytes(10), TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
		Assert.Equal(Stored, screenshot);
		await f.Storage.Received(1).SaveAsync(Arg.Any<Stream>(), extension, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UploadAsync_AcceptedPicture_StoresTheWholeUploadedContent()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 10, 10));
		var uploaded = Enumerable.Range(0, 1000).Select(i => (byte)(i % 251)).ToArray();
		byte[]? savedBytes = null;
		f.Storage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
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
	public async Task UploadAsync_ExactlyTheByteCeiling_IsAccepted()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 100, 100));
		var screenshots = await f.UploaderAsync();

		var (_, result) = await screenshots.UploadAsync(Bytes(ScopedServerScreenshots.MaxScreenshotBytes),
			TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
	}

	[Fact]
	public async Task UploadAsync_AcceptedPicture_DoesNotDisposeTheCallersStream()
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
	[InlineData(ScopedServerScreenshots.MaxScreenshotWidth, ScopedServerScreenshots.MaxScreenshotHeight)]
	public async Task UploadAsync_PictureAtTheSmallestAndLargestSide_IsAccepted(int width, int height)
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
	public async Task UploadAsync_OneByteOverTheCeiling_ReturnsTooLargeAndStoresNothing()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 100, 100));
		var screenshots = await f.UploaderAsync();

		var (screenshot, result) = await screenshots.UploadAsync(
			Bytes(ScopedServerScreenshots.MaxScreenshotBytes + 1), TestContext.Current.CancellationToken);

		Assert.Null(screenshot);
		Assert.Equal(ServerOutcome.TooLarge, result.Outcome);
		f.AssertNothingStored();
	}

	[Fact]
	public async Task UploadAsync_FarOverTheCeiling_ReturnsTooLarge()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 100, 100));
		var screenshots = await f.UploaderAsync();

		var (_, result) = await screenshots.UploadAsync(
			Bytes(ScopedServerScreenshots.MaxScreenshotBytes * 2), TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.TooLarge, result.Outcome);
	}

	[Fact]
	public async Task UploadAsync_ContentOfAnUnsupportedFormat_ReturnsInvalidImageAndStoresNothing()
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
	public async Task UploadAsync_EmptyContent_IsNotAPictureAndStoresNothing()
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
	public async Task UploadAsync_PictureWithANonPositiveSide_ReturnsInvalidImage(int width, int height)
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", width, height));
		var screenshots = await f.UploaderAsync();

		var (_, result) = await screenshots.UploadAsync(Bytes(10), TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.InvalidImage, result.Outcome);
		f.AssertNothingStored();
	}

	[Theory]
	[InlineData(ScopedServerScreenshots.MaxScreenshotWidth + 1, 100)]
	[InlineData(100, ScopedServerScreenshots.MaxScreenshotHeight + 1)]
	[InlineData(ScopedServerScreenshots.MaxScreenshotWidth + 1, ScopedServerScreenshots.MaxScreenshotHeight + 1)]
	public async Task UploadAsync_PictureOverThePixelCeilingOnASide_ReturnsDimensionsTooLargeAndStoresNothing(
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
	public async Task UploadAsync_OversizedUpload_IsNeverShownToTheInspector()
	{
		using var f = new Fixture();
		f.InspectedAs(new ScreenshotInfo("png", 100, 100));
		var screenshots = await f.UploaderAsync();

		await screenshots.UploadAsync(Bytes(ScopedServerScreenshots.MaxScreenshotBytes + 1),
			TestContext.Current.CancellationToken);

		await f.Inspector.DidNotReceiveWithAnyArgs().InspectAsync(default!);
	}

	#endregion

	#region Delete

	[Fact]
	public async Task DeleteAsync_WithScreenshotDelete_IsOkAndRemovesTheScreenshot()
	{
		using var f = new Fixture();
		var screenshots = await f.ScreenshotsOfAsync(UserPermissionsGroups.Default | UserPermissions.ScreenshotDelete);

		var result = await screenshots.DeleteAsync(Stored, TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
		await f.Storage.Received(1).DeleteAsync(Stored, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task DeleteAsync_WithoutScreenshotDelete_IsDeniedNamingItAndRemovesNothing()
	{
		using var f = new Fixture();
		var screenshots = await f.ScreenshotsOfAsync(UserPermissionsGroups.Default & ~UserPermissions.ScreenshotDelete);

		var result = await screenshots.DeleteAsync(Stored, TestContext.Current.CancellationToken);

		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.ScreenshotDelete, result.Missing);
		await f.Storage.DidNotReceiveWithAnyArgs().DeleteAsync(default);
	}

	[Fact]
	public async Task DeleteAsync_HoldingOnlyScreenshotUpload_IsStillDenied()
	{
		using var f = new Fixture();
		var screenshots = await f.ScreenshotsOfAsync(
			(UserPermissionsGroups.Default & ~UserPermissions.ScreenshotDelete) | UserPermissions.ScreenshotUpload);

		var result = await screenshots.DeleteAsync(Stored, TestContext.Current.CancellationToken);

		Assert.Equal(UserPermissions.ScreenshotDelete, result.Missing);
	}

	[Fact]
	public async Task DeleteAsync_ScreenshotDeleteSuspendedByRestriction_IsDenied()
	{
		using var f = new Fixture();
		var user = await f.App.CreateUserAsync("NoDeleter", "password",
			UserPermissionsGroups.Default | UserPermissions.ScreenshotDelete);
		f.App.AddRestriction(user, UserPermissions.ScreenshotDelete);
		var screenshots = f.App.Server.ScopeTo(await f.App.LoginAsync(user)).Screenshots;

		var result = await screenshots.DeleteAsync(Stored, TestContext.Current.CancellationToken);

		Assert.Equal(UserPermissions.ScreenshotDelete, result.Missing);
	}

	#endregion
}