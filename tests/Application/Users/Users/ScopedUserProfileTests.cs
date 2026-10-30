using System.Text;
using Basil.Application.Content.Capabilities;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Users;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Basil.Application.Users.Tests.Users;

/// <summary>
///     Changing a profile: a user changes their own; staff change another account's under hierarchy; the area locked
///     for profiles refuses everyone who does not manage accounts; BasilBot's profile changes like anyone's. An avatar
///     is a PNG or JPEG picture within the configured byte and pixel limits, told from its content.
/// </summary>
public sealed class ScopedUserProfileTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>An avatar store that keeps the images in memory.</summary>
	private sealed class MemoryAvatars : IUserAvatarStorage
	{
		public Dictionary<int, byte[]> Images { get; } = [];

		public async Task SaveAsync(UserRecord user, Stream content, CancellationToken cancellationToken = default)
		{
			using var copy = new MemoryStream();
			await content.CopyToAsync(copy, cancellationToken);
			Images[user.Id] = copy.ToArray();
		}

		public Task<Stream?> OpenAsync(UserRecord user, CancellationToken cancellationToken = default)
		{
			return Task.FromResult<Stream?>(Images.TryGetValue(user.Id, out var image) ? new MemoryStream(image) : null);
		}

		public Task DeleteAsync(UserRecord user, CancellationToken cancellationToken = default)
		{
			Images.Remove(user.Id);
			return Task.CompletedTask;
		}
	}

	/// <summary>A stream that counts the bytes handed to its reader.</summary>
	private sealed class CountingStream(byte[] content) : MemoryStream(content)
	{
		public long BytesRead { get; private set; }

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			var read = await base.ReadAsync(buffer, cancellationToken);
			BytesRead += read;
			return read;
		}
	}

	private static IScreenshotInspector InspectorOf(TestApplication app)
	{
		return (IScreenshotInspector)app.Provider.GetRequiredService(typeof(IScreenshotInspector));
	}

	private static void InspectedAs(TestApplication app, ScreenshotInfo? info)
	{
		InspectorOf(app).InspectAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(info);
	}

	private static async Task<(TestApplication App, MemoryAvatars Avatars)> StartAsync(Locks locks = Locks.None,
		ServerSettings.UploadSettings? uploads = null)
	{
		var avatars = new MemoryAvatars();
		var app = new TestApplication(services => services.AddSingleton<IUserAvatarStorage>(avatars));
		InspectedAs(app, new ScreenshotInfo("png", 64, 64));
		await app.SetSettingsAsync(new ServerSettings { Locks = locks, Uploads = uploads ?? new() });
		return (app, avatars);
	}

	private static Task<byte[]> AssetAsync(string name)
	{
		return File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Images", name), Token);
	}

	[Fact]
	public async Task SetCountryAsync_OfOwnProfile_ChangesTheCountry()
	{
		var (app, _) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("Traveller", "password");
		var by = await app.LoginAsync(user);

		var result = await app.Users.Of(user).ScopeTo(by).Profile.SetCountryAsync(Country.Vn, Token);

		Assert.True(result.IsOk);
		Assert.Equal(Country.Vn, user.Value.Country);
	}

	[Fact]
	public async Task SetCountryAsync_WithAValueNoCountryHas_IsRefused()
	{
		var (app, _) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("Nowhere", "password");
		var by = await app.LoginAsync(user);

		var result = await app.Users.Of(user).ScopeTo(by).Profile.SetCountryAsync((Country)0, Token);

		Assert.Equal(UserOutcome.InvalidCountry, result.Outcome);
	}

	[Fact]
	public async Task RenameAsync_OfOwnProfile_GivesTheNewName()
	{
		var (app, _) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("OldName", "password");
		var by = await app.LoginAsync(user);

		var result = await app.Users.Of(user).ScopeTo(by).Profile.RenameAsync("NewName", Token);

		Assert.True(result.IsOk);
		Assert.Equal("NewName", user.Value.Name);
	}

	[Fact]
	public async Task RenameAsync_ToANameAnotherAccountHas_IsRefused()
	{
		var (app, _) = await StartAsync();
		using var _ = app;
		await app.CreateUserAsync("Taken", "password");
		var user = await app.CreateUserAsync("Wanter", "password");
		var by = await app.LoginAsync(user);

		var result = await app.Users.Of(user).ScopeTo(by).Profile.RenameAsync("taken", Token);

		Assert.Equal(UserOutcome.NameTaken, result.Outcome);
		Assert.Equal("Wanter", user.Value.Name);
	}

	[Fact]
	public async Task RenameAsync_ToAnInvalidName_IsRefused()
	{
		var (app, _) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("Valid", "password");
		var by = await app.LoginAsync(user);

		var result = await app.Users.Of(user).ScopeTo(by).Profile.RenameAsync("", Token);

		Assert.Equal(UserOutcome.InvalidName, result.Outcome);
	}

	[Fact]
	public async Task SetAvatarAsync_ThenRemoveAvatarAsync_StoresThenRemovesTheImage()
	{
		var (app, avatars) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("Pictured", "password");
		var by = await app.LoginAsync(user);
		var profile = app.Users.Of(user).ScopeTo(by).Profile;

		Assert.True((await profile.SetAvatarAsync(new MemoryStream([1, 2, 3]), Token)).IsOk);
		Assert.Equal([1, 2, 3], avatars.Images[user.Id]);
		Assert.True((await profile.RemoveAvatarAsync(Token)).IsOk);
		Assert.Empty(avatars.Images);
	}

	[Fact]
	public async Task SetAvatarAsync_APngPicture_IsStored()
	{
		var (app, avatars) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("Pictured", "password");
		var by = await app.LoginAsync(user);
		var png = await AssetAsync("avatar-default.png");

		var result = await app.Users.Of(user).ScopeTo(by).Profile.SetAvatarAsync(new MemoryStream(png), Token);

		Assert.True(result.IsOk);
		Assert.Equal(png, avatars.Images[user.Id]);
	}

	[Fact]
	public async Task SetAvatarAsync_AJpegPicture_IsStored()
	{
		var (app, avatars) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("Pictured", "password");
		var by = await app.LoginAsync(user);
		var jpeg = await AssetAsync("background-1920x1200.jpg");
		InspectedAs(app, new ScreenshotInfo("jpg", 1920, 1200));

		var result = await app.Users.Of(user).ScopeTo(by).Profile.SetAvatarAsync(new MemoryStream(jpeg), Token);

		Assert.True(result.IsOk);
		Assert.Equal(jpeg, avatars.Images[user.Id]);
	}

	[Fact]
	public async Task SetAvatarAsync_ContentThatIsNotAPicture_IsRefusedAndLeavesThePreviousAvatar()
	{
		var (app, avatars) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("Pictured", "password");
		var by = await app.LoginAsync(user);
		var profile = app.Users.Of(user).ScopeTo(by).Profile;
		var previous = await AssetAsync("avatar-default.png");
		Assert.True((await profile.SetAvatarAsync(new MemoryStream(previous), Token)).IsOk);

		InspectedAs(app, null);
		var html = Encoding.UTF8.GetBytes("<!doctype html><script>alert(1)</script>");
		var result = await profile.SetAvatarAsync(new MemoryStream(html), Token);

		Assert.Equal(UserOutcome.InvalidImage, result.Outcome);
		Assert.Equal(previous, avatars.Images[user.Id]);
	}

	[Theory]
	[InlineData(0, 8)]
	[InlineData(8, 0)]
	public async Task SetAvatarAsync_PictureWithANonPositiveSide_IsRefusedAsInvalidImage(int width, int height)
	{
		var (app, avatars) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("Pictured", "password");
		var by = await app.LoginAsync(user);
		InspectedAs(app, new ScreenshotInfo("png", width, height));

		var result = await app.Users.Of(user).ScopeTo(by).Profile
			.SetAvatarAsync(new MemoryStream(await AssetAsync("avatar-default.png")), Token);

		Assert.Equal(UserOutcome.InvalidImage, result.Outcome);
		Assert.Empty(avatars.Images);
	}

	[Theory]
	[InlineData(9, 8)]
	[InlineData(8, 9)]
	public async Task SetAvatarAsync_PictureOverThePixelLimit_IsRefusedAndStoresNothing(int width, int height)
	{
		var (app, avatars) = await StartAsync(uploads: new ServerSettings.UploadSettings { AvatarMaxSide = 8 });
		using var _ = app;
		var user = await app.CreateUserAsync("Pictured", "password");
		var by = await app.LoginAsync(user);
		InspectedAs(app, new ScreenshotInfo("png", width, height));

		var result = await app.Users.Of(user).ScopeTo(by).Profile
			.SetAvatarAsync(new MemoryStream(await AssetAsync("avatar-default.png")), Token);

		Assert.Equal(UserOutcome.DimensionsTooLarge, result.Outcome);
		Assert.Empty(avatars.Images);
	}

	[Fact]
	public async Task SetAvatarAsync_ContentOverTheByteLimit_IsRefusedWithoutReadingItAll()
	{
		var (app, avatars) = await StartAsync(uploads: new ServerSettings.UploadSettings { AvatarMaxSize = 64 });
		using var _ = app;
		var user = await app.CreateUserAsync("Pictured", "password");
		var by = await app.LoginAsync(user);
		var content = new CountingStream(new byte[256]);

		var result = await app.Users.Of(user).ScopeTo(by).Profile.SetAvatarAsync(content, Token);

		Assert.Equal(UserOutcome.TooLarge, result.Outcome);
		Assert.Equal(65, content.BytesRead);
		Assert.Empty(avatars.Images);
	}

	[Fact]
	public async Task EveryChange_OfAnotherAccount_ByAUserWithoutUserManage_NamesTheMissingPermission()
	{
		var (app, avatars) = await StartAsync();
		using var _ = app;
		var nosy = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));
		var user = await app.CreateUserAsync("Victim", "password");
		var profile = app.Users.Of(user).ScopeTo(nosy).Profile;

		Assert.Equal(UserPermissions.UserManage, (await profile.RenameAsync("Hacked", Token)).Missing);
		Assert.Equal(UserPermissions.UserManage, (await profile.SetCountryAsync(Country.Au, Token)).Missing);
		Assert.Equal(UserPermissions.UserManage,
			(await profile.SetAvatarAsync(new MemoryStream([1]), Token)).Missing);
		Assert.Equal(UserPermissions.UserManage, (await profile.RemoveAvatarAsync(Token)).Missing);
		Assert.Equal("Victim", user.Value.Name);
		Assert.Empty(avatars.Images);
	}

	[Fact]
	public async Task EveryChange_OfAnotherAccount_ByStaffOfEqualRank_IsRefused()
	{
		var (app, _) = await StartAsync();
		using var _ = app;
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));
		var peer = await app.CreateUserAsync("Peer", "password", UserPermissionsGroups.All);
		var profile = app.Users.Of(peer).ScopeTo(admin).Profile;

		Assert.Equal(UserOutcome.NotOutranking, (await profile.RenameAsync("Hacked", Token)).Outcome);
		Assert.Equal(UserOutcome.NotOutranking, (await profile.SetCountryAsync(Country.Au, Token)).Outcome);
	}

	[Fact]
	public async Task RenameAsync_ByStaffOfHigherRank_RenamesTheAccount()
	{
		var (app, _) = await StartAsync();
		using var _ = app;
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));
		var user = await app.CreateUserAsync("Offender", "password");

		var result = await app.Users.Of(user).ScopeTo(admin).Profile.RenameAsync("Reformed", Token);

		Assert.True(result.IsOk);
		Assert.Equal("Reformed", user.Value.Name);
	}

	[Fact]
	public async Task EveryChange_WhileProfilesAreLocked_IsRefusedForAUserWhoDoesNotManageAccounts()
	{
		var (app, avatars) = await StartAsync(Locks.Profiles);
		using var _ = app;
		var user = await app.CreateUserAsync("Frozen", "password");
		var by = await app.LoginAsync(user);
		var profile = app.Users.Of(user).ScopeTo(by).Profile;

		Assert.Equal(UserOutcome.Locked, (await profile.RenameAsync("Thawed", Token)).Outcome);
		Assert.Equal(UserOutcome.Locked, (await profile.SetCountryAsync(Country.Au, Token)).Outcome);
		Assert.Equal(UserOutcome.Locked, (await profile.SetAvatarAsync(new MemoryStream([1]), Token)).Outcome);
		Assert.Equal(UserOutcome.Locked, (await profile.RemoveAvatarAsync(Token)).Outcome);
		Assert.Equal("Frozen", user.Value.Name);
		Assert.Empty(avatars.Images);
	}

	[Fact]
	public async Task EveryChange_WhileProfilesAreLocked_IsAllowedForAUserWhoManagesAccounts()
	{
		var (app, _) = await StartAsync(Locks.Profiles);
		using var _ = app;
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));
		var user = await app.CreateUserAsync("Frozen", "password");

		var result = await app.Users.Of(user).ScopeTo(admin).Profile.RenameAsync("Thawed", Token);

		Assert.True(result.IsOk);
	}

	[Fact]
	public async Task BasilBot_CanBeRenamedAndGivenAnAvatarLikeAnyUser()
	{
		var (app, avatars) = await StartAsync();
		using var _ = app;
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));
		var bot = await app.SeedBotAsync();
		var profile = app.Users.Of(bot).ScopeTo(admin).Profile;

		Assert.True((await profile.RenameAsync("Basilisk", Token)).IsOk);
		Assert.True((await profile.SetCountryAsync(Country.Vn, Token)).IsOk);
		Assert.True((await profile.SetAvatarAsync(new MemoryStream([9]), Token)).IsOk);
		Assert.Equal("Basilisk", bot.Value.Name);
		Assert.Equal([9], avatars.Images[bot.Id]);
	}
}
