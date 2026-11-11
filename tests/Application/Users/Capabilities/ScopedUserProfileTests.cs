using System.Text;
using Basil.Application.Content.Persistence;
using Basil.Application.Content.Ports;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Storage;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Basil.Application.Users.Tests.Capabilities;

/// <summary>
///     Changing a profile: a user changes their own; staff change another account's under hierarchy; the area locked
///     for profiles refuses everyone who does not manage accounts; BasilBot's profile changes like anyone's. An avatar
///     is a PNG or JPEG picture within the configured byte and pixel limits, told from its content.
/// </summary>
public sealed class ScopedUserProfileTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

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
		await app.SetSettingsAsync(new ServerSettings
			{ Locks = locks, Uploads = uploads ?? new ServerSettings.UploadSettings() });
		return (app, avatars);
	}

	private static Task<byte[]> AssetAsync(string name)
	{
		return File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "Images", name), Token);
	}

	[Fact]
	public async Task SetCountryOwn_Changed()
	{
		var (app, _) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("Traveller", "password");
		var actor = await app.LoginAsync(user);

		var result = await app.Users.Of(user).ScopeTo(actor).Profile.SetCountryAsync(Country.Vn);

		Assert.True(result.IsOk);
		Assert.Equal(Country.Vn, user.Value.Country);
	}

	[Fact]
	public async Task SetCountryUnknown_Refused()
	{
		var (app, _) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("Nowhere", "password");
		var actor = await app.LoginAsync(user);

		var result = await app.Users.Of(user).ScopeTo(actor).Profile.SetCountryAsync(0);

		Assert.Equal(UserOutcome.InvalidCountry, result.Outcome);
	}

	[Fact]
	public async Task RenameOwn_NewName()
	{
		var (app, _) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("OldName", "password");
		var actor = await app.LoginAsync(user);

		var result = await app.Users.Of(user).ScopeTo(actor).Profile.RenameAsync("NewName");

		Assert.True(result.IsOk);
		Assert.Equal("NewName", user.Value.Name);
	}

	[Fact]
	public async Task RenameTaken_Refused()
	{
		var (app, _) = await StartAsync();
		using var _ = app;
		await app.CreateUserAsync("Taken", "password");
		var user = await app.CreateUserAsync("Wanter", "password");
		var actor = await app.LoginAsync(user);

		var result = await app.Users.Of(user).ScopeTo(actor).Profile.RenameAsync("taken");

		Assert.Equal(UserOutcome.NameTaken, result.Outcome);
		Assert.Equal("Wanter", user.Value.Name);
	}

	[Fact]
	public async Task RenameInvalid_Refused()
	{
		var (app, _) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("Valid", "password");
		var actor = await app.LoginAsync(user);

		var result = await app.Users.Of(user).ScopeTo(actor).Profile.RenameAsync("");

		Assert.Equal(UserOutcome.InvalidName, result.Outcome);
	}

	[Fact]
	public async Task AvatarRemoved_ImageGone()
	{
		var (app, avatars) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("Pictured", "password");
		var actor = await app.LoginAsync(user);
		var profile = app.Users.Of(user).ScopeTo(actor).Profile;

		Assert.True((await profile.SetAvatarAsync(new MemoryStream([1, 2, 3]), Token)).IsOk);
		Assert.Equal([1, 2, 3], avatars.Images[user.Id]);
		Assert.True((await profile.RemoveAvatarAsync()).IsOk);
		Assert.Empty(avatars.Images);
	}

	[Fact]
	public async Task AvatarPng_Stored()
	{
		var (app, avatars) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("Pictured", "password");
		var actor = await app.LoginAsync(user);
		var png = await AssetAsync("avatar-default.png");

		var result = await app.Users.Of(user).ScopeTo(actor).Profile.SetAvatarAsync(new MemoryStream(png), Token);

		Assert.True(result.IsOk);
		Assert.Equal(png, avatars.Images[user.Id]);
	}

	[Fact]
	public async Task AvatarJpeg_Stored()
	{
		var (app, avatars) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("Pictured", "password");
		var actor = await app.LoginAsync(user);
		var jpeg = await AssetAsync("background-1920x1200.jpg");
		InspectedAs(app, new ScreenshotInfo("jpg", 1920, 1200));

		var result = await app.Users.Of(user).ScopeTo(actor).Profile.SetAvatarAsync(new MemoryStream(jpeg), Token);

		Assert.True(result.IsOk);
		Assert.Equal(jpeg, avatars.Images[user.Id]);
	}

	[Fact]
	public async Task AvatarNotPicture_Refused()
	{
		var (app, avatars) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("Pictured", "password");
		var actor = await app.LoginAsync(user);
		var profile = app.Users.Of(user).ScopeTo(actor).Profile;
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
	public async Task AvatarNonPositive_InvalidImage(int width, int height)
	{
		var (app, avatars) = await StartAsync();
		using var _ = app;
		var user = await app.CreateUserAsync("Pictured", "password");
		var actor = await app.LoginAsync(user);
		InspectedAs(app, new ScreenshotInfo("png", width, height));

		var result = await app.Users.Of(user).ScopeTo(actor).Profile
			.SetAvatarAsync(new MemoryStream(await AssetAsync("avatar-default.png")), Token);

		Assert.Equal(UserOutcome.InvalidImage, result.Outcome);
		Assert.Empty(avatars.Images);
	}

	[Theory]
	[InlineData(9, 8)]
	[InlineData(8, 9)]
	public async Task AvatarOverPixels_Refused(int width, int height)
	{
		var (app, avatars) = await StartAsync(uploads: new ServerSettings.UploadSettings { AvatarMaxSide = 8 });
		using var _ = app;
		var user = await app.CreateUserAsync("Pictured", "password");
		var actor = await app.LoginAsync(user);
		InspectedAs(app, new ScreenshotInfo("png", width, height));

		var result = await app.Users.Of(user).ScopeTo(actor).Profile
			.SetAvatarAsync(new MemoryStream(await AssetAsync("avatar-default.png")), Token);

		Assert.Equal(UserOutcome.DimensionsTooLarge, result.Outcome);
		Assert.Empty(avatars.Images);
	}

	[Fact]
	public async Task AvatarOverBytes_Refused()
	{
		var (app, avatars) = await StartAsync(uploads: new ServerSettings.UploadSettings { AvatarMaxSize = 64 });
		using var _ = app;
		var user = await app.CreateUserAsync("Pictured", "password");
		var actor = await app.LoginAsync(user);
		var content = new CountingStream(new byte[256]);

		var result = await app.Users.Of(user).ScopeTo(actor).Profile.SetAvatarAsync(content, Token);

		Assert.Equal(UserOutcome.TooLarge, result.Outcome);
		Assert.Equal(65, content.BytesRead);
		Assert.Empty(avatars.Images);
	}

	[Fact]
	public async Task OtherNoManage_Named()
	{
		var (app, avatars) = await StartAsync();
		using var _ = app;
		var nosy = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));
		var user = await app.CreateUserAsync("Victim", "password");
		var profile = app.Users.Of(user).ScopeTo(nosy).Profile;

		Assert.Equal(UserPrivileges.Owner, (await profile.RenameAsync("Hacked")).Missing);
		Assert.Equal(UserPrivileges.Owner, (await profile.SetCountryAsync(Country.Au)).Missing);
		Assert.Equal(UserPrivileges.Owner,
			(await profile.SetAvatarAsync(new MemoryStream([1]), Token)).Missing);
		Assert.Equal(UserPrivileges.Owner, (await profile.RemoveAvatarAsync()).Missing);
		Assert.Equal("Victim", user.Value.Name);
		Assert.Empty(avatars.Images);
	}

	[Fact]
	public async Task OtherEqualRank_Refused()
	{
		var (app, _) = await StartAsync();
		using var _ = app;
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPrivilegesExtensions.All));
		var peer = await app.CreateUserAsync("Peer", "password", UserPrivilegesExtensions.All);
		var profile = app.Users.Of(peer).ScopeTo(admin).Profile;

		Assert.Equal(UserOutcome.NotOutranking, (await profile.RenameAsync("Hacked")).Outcome);
		Assert.Equal(UserOutcome.NotOutranking, (await profile.SetCountryAsync(Country.Au)).Outcome);
	}

	[Fact]
	public async Task RenameHigherRank_Renamed()
	{
		var (app, _) = await StartAsync();
		using var _ = app;
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPrivilegesExtensions.All));
		var user = await app.CreateUserAsync("Offender", "password");

		var result = await app.Users.Of(user).ScopeTo(admin).Profile.RenameAsync("Reformed");

		Assert.True(result.IsOk);
		Assert.Equal("Reformed", user.Value.Name);
	}

	[Fact]
	public async Task LockedProfiles_Refused()
	{
		var (app, avatars) = await StartAsync(Locks.Profiles);
		using var _ = app;
		var user = await app.CreateUserAsync("Frozen", "password");
		var actor = await app.LoginAsync(user);
		var profile = app.Users.Of(user).ScopeTo(actor).Profile;

		Assert.Equal(UserOutcome.Locked, (await profile.RenameAsync("Thawed")).Outcome);
		Assert.Equal(UserOutcome.Locked, (await profile.SetCountryAsync(Country.Au)).Outcome);
		Assert.Equal(UserOutcome.Locked, (await profile.SetAvatarAsync(new MemoryStream([1]), Token)).Outcome);
		Assert.Equal(UserOutcome.Locked, (await profile.RemoveAvatarAsync()).Outcome);
		Assert.Equal("Frozen", user.Value.Name);
		Assert.Empty(avatars.Images);
	}

	[Fact]
	public async Task LockedProfiles_ManagerAllowed()
	{
		var (app, _) = await StartAsync(Locks.Profiles);
		using var _ = app;
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPrivilegesExtensions.All));
		var user = await app.CreateUserAsync("Frozen", "password");

		var result = await app.Users.Of(user).ScopeTo(admin).Profile.RenameAsync("Thawed");

		Assert.True(result.IsOk);
	}

	[Fact]
	public async Task BasilBot_RenamedAndAvatar()
	{
		var (app, avatars) = await StartAsync();
		using var _ = app;
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPrivilegesExtensions.All));
		var bot = await app.SeedBotAsync();
		var profile = app.Users.Of(bot).ScopeTo(admin).Profile;

		Assert.True((await profile.RenameAsync("Basilisk")).IsOk);
		Assert.True((await profile.SetCountryAsync(Country.Vn)).IsOk);
		Assert.True((await profile.SetAvatarAsync(new MemoryStream([9]), Token)).IsOk);
		Assert.Equal("Basilisk", bot.Value.Name);
		Assert.Equal([9], avatars.Images[bot.Id]);
	}

	/// <summary>An avatar store that keeps the images in memory.</summary>
	private sealed class MemoryAvatars : IUserAvatarStorage
	{
		public Dictionary<int, byte[]> Images { get; } = [];

		public async Task SaveAsync(UserRecord user, Stream content)
		{
			using var copy = new MemoryStream();
			await content.CopyToAsync(copy);
			Images[user.Id] = copy.ToArray();
		}

		public Task<Stream?> OpenAsync(UserRecord user, CancellationToken ct = default)
		{
			return Task.FromResult<Stream?>(Images.TryGetValue(user.Id, out var image)
				? new MemoryStream(image)
				: null);
		}

		public Task DeleteAsync(UserRecord user)
		{
			Images.Remove(user.Id);
			return Task.CompletedTask;
		}
	}

	/// <summary>A stream that counts the bytes handed to its reader.</summary>
	private sealed class CountingStream(byte[] content) : MemoryStream(content)
	{
		public long BytesRead { get; private set; }

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
			CancellationToken ct = default)
		{
			var read = await base.ReadAsync(buffer, ct);
			BytesRead += read;
			return read;
		}
	}
}