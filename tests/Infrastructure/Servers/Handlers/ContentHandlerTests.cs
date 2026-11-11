using Basil.Application.Common.Settings;
using Basil.Application.Content.Storage;
using Basil.Application.Servers.Messages;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Content;
using Basil.Domain.Users;
using Basil.Infrastructure.Servers.Handlers;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;
using Basil.Application.Servers.Objects;

namespace Basil.Infrastructure.Servers.Tests.Handlers;

/// <summary>The messages that manage the FAQ, the menu, the settings, the screenshots and the notifications of the server.</summary>
public sealed class ContentHandlerTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task FaqSaveAllowed_StoresText()
	{
		var faqs = new InMemoryFaqs();
		using var app = new TestApplication(services => services.AddSingleton<IFaqStorage>(faqs));
		var admin = await AdminAsync(app);

		var result = await new SaveFaqHandler(app.Server).Handle(new SaveFaq(admin, "rules", "Be kind."), Token);

		Assert.True(result == ServerOutcome.Ok);
		Assert.Equal("Be kind.", faqs.Entries["rules"]);
	}

	[Fact]
	public async Task FaqSaveDenied_NothingStored()
	{
		var faqs = new InMemoryFaqs();
		using var app = new TestApplication(services => services.AddSingleton<IFaqStorage>(faqs));
		var plain = await PlainAsync(app);

		var result = await new SaveFaqHandler(app.Server).Handle(new SaveFaq(plain, "rules", "Be kind."), Token);

		Assert.Equal(ServerOutcome.Denied, result);
		Assert.Empty(faqs.Entries);
	}

	[Fact]
	public async Task FaqDeleteAllowed_EntryRemoved()
	{
		var faqs = new InMemoryFaqs();
		using var app = new TestApplication(services => services.AddSingleton<IFaqStorage>(faqs));
		var admin = await AdminAsync(app);
		await new SaveFaqHandler(app.Server).Handle(new SaveFaq(admin, "rules", "Be kind."), Token);

		var result = await new DeleteFaqHandler(app.Server).Handle(new DeleteFaq(admin, "rules"), Token);

		Assert.True(result == ServerOutcome.Ok);
		Assert.Empty(faqs.Entries);
	}

	[Fact]
	public async Task FaqDeleteDenied_Refused()
	{
		using var app = new TestApplication();
		var plain = await PlainAsync(app);

		var result = await new DeleteFaqHandler(app.Server).Handle(new DeleteFaq(plain, "rules"), Token);

		Assert.Equal(ServerOutcome.Denied, result);
	}

	[Fact]
	public async Task IconClearDenied_Refused()
	{
		using var app = new TestApplication();

		var result = await new ClearMenuIconHandler(app.Server).Handle(new ClearMenuIcon(await PlainAsync(app)), Token);

		Assert.Equal(ServerOutcome.Denied, result);
	}

	[Fact]
	public async Task IconSetDenied_Refused()
	{
		using var app = new TestApplication();

		var result = await new SetMenuIconHandler(app.Server)
			.Handle(new SetMenuIcon(await PlainAsync(app), "icon", new MemoryStream(), null), Token);

		Assert.Equal(ServerOutcome.Denied, result);
	}

	[Fact]
	public async Task BannerAddDenied_NothingAdded()
	{
		using var app = new TestApplication();

		var (result, banner) = await new AddMenuBannerHandler(app.Server)
			.Handle(new AddMenuBanner(await PlainAsync(app), "banner", new MemoryStream(),
				new Uri("https://example.test/banner.png"), null, null), Token);

		Assert.Equal(ServerOutcome.Denied, result);
		Assert.Null(banner);
	}

	[Fact]
	public async Task SeasonalAddDenied_NothingAdded()
	{
		using var app = new TestApplication();

		var (result, image) = await new AddMenuSeasonalHandler(app.Server)
			.Handle(new AddMenuSeasonal(await PlainAsync(app), "season", new MemoryStream()), Token);

		Assert.Equal(ServerOutcome.Denied, result);
		Assert.Null(image);
	}

	[Fact]
	public async Task SeasonalDeleteDenied_Refused()
	{
		using var app = new TestApplication();

		var result = await new DeleteMenuSeasonalHandler(app.Server)
			.Handle(new DeleteMenuSeasonal(await PlainAsync(app), new ContentReference("season.png")), Token);

		Assert.Equal(ServerOutcome.Denied, result);
	}

	[Fact]
	public async Task SettingsGetAllowed_ReturnsSettings()
	{
		using var app = new TestApplication();

		var (result, settings) = await new GetSettingsHandler(app.Server).Handle(new GetSettings(await AdminAsync(app)), Token);

		Assert.True(result == ServerOutcome.Ok);
		Assert.NotNull(settings);
	}

	[Fact]
	public async Task SettingsGetDenied_Nothing()
	{
		using var app = new TestApplication();

		var (result, settings) = await new GetSettingsHandler(app.Server).Handle(new GetSettings(await PlainAsync(app)), Token);

		Assert.Equal(ServerOutcome.Denied, result);
		Assert.Null(settings);
	}

	[Fact]
	public async Task SettingsUpdateAllowed_Replaced()
	{
		using var app = new TestApplication();
		var admin = await AdminAsync(app);
		var (_, current) = await new GetSettingsHandler(app.Server).Handle(new GetSettings(admin), Token);
		var changed = current! with { Accounts = current.Accounts with { DefaultPrivileges = current.Accounts.DefaultPrivileges } };

		var result = await new UpdateSettingsHandler(app.Server).Handle(new UpdateSettings(admin, changed), Token);

		Assert.True(result == ServerOutcome.Ok);
	}

	[Fact]
	public async Task SettingsUpdateDenied_Refused()
	{
		using var app = new TestApplication();
		var (_, current) = await new GetSettingsHandler(app.Server).Handle(new GetSettings(await AdminAsync(app)), Token);

		var result = await new UpdateSettingsHandler(app.Server)
			.Handle(new UpdateSettings(await PlainAsync(app), current!), Token);

		Assert.Equal(ServerOutcome.Denied, result);
	}

	[Fact]
	public async Task ScreenshotListAllowed_Lists()
	{
		using var app = new TestApplication();

		var (result, screenshots) = await new ListScreenshotsHandler(app.Server)
			.Handle(new ListScreenshots(await AdminAsync(app)), Token);

		Assert.True(result == ServerOutcome.Ok);
		Assert.Empty(screenshots);
	}

	[Fact]
	public async Task ScreenshotListDenied_Refused()
	{
		using var app = new TestApplication();

		var (result, _) = await new ListScreenshotsHandler(app.Server)
			.Handle(new ListScreenshots(await PlainAsync(app)), Token);

		Assert.Equal(ServerOutcome.Denied, result);
	}

	[Fact]
	public async Task ScreenshotDeleteDenied_Refused()
	{
		using var app = new TestApplication();

		var result = await new DeleteScreenshotHandler(app.Server)
			.Handle(new DeleteScreenshot(await PlainAsync(app), new ContentReference("shot.png")), Token);

		Assert.Equal(ServerOutcome.Denied, result);
	}

	[Fact]
	public async Task ScreenshotUploadDenied_NothingStored()
	{
		using var app = new TestApplication();

		var (screenshot, result) = await new UploadScreenshotHandler(app.Server)
			.Handle(new UploadScreenshot(await PlainAsync(app), new MemoryStream()), Token);

		Assert.Equal(ServerOutcome.Denied, result);
		Assert.Null(screenshot);
	}

	[Fact]
	public async Task BeatmapsetUploadDenied_NothingImported()
	{
		using var app = new TestApplication();
		await app.SetSettingsAsync(new ServerSettings { Locks = Locks.None });

		var (result, import) = await new UploadBeatmapsetHandler(app.Server)
			.Handle(new UploadBeatmapset(await PlainAsync(app), new MemoryStream()), Token);

		Assert.Equal(ServerOutcome.Denied, result);
		Assert.Null(import);
	}

	[Fact]
	public async Task NotifyEveryone_CountsReachedClients()
	{
		using var app = new TestApplication();
		var admin = await AdminAsync(app);
		await RoomFixture.PlayerAsync(app, "One");
		await RoomFixture.PlayerAsync(app, "Two");

		var (result, sent) = await new NotifyEveryoneHandler(app.Server).Handle(new NotifyEveryone(admin, "hello"), Token);

		Assert.True(result == ServerOutcome.Ok);
		Assert.Equal(2, sent);
	}

	[Fact]
	public async Task NotifyEveryoneDenied_ReachesNobody()
	{
		using var app = new TestApplication();
		await RoomFixture.PlayerAsync(app, "One");

		var (result, sent) = await new NotifyEveryoneHandler(app.Server)
			.Handle(new NotifyEveryone(await PlainAsync(app), "hello"), Token);

		Assert.Equal(ServerOutcome.Denied, result);
		Assert.Equal(0, sent);
	}

	[Fact]
	public async Task NotifyUsers_OnlyNamedUsers()
	{
		using var app = new TestApplication();
		var admin = await AdminAsync(app);
		var one = await RoomFixture.PlayerAsync(app, "One");
		await RoomFixture.PlayerAsync(app, "Two");

		var (result, sent) = await new NotifyUsersHandler(app.Server)
			.Handle(new NotifyUsers(admin, "hello", [one.Record.Id]), Token);

		Assert.True(result == ServerOutcome.Ok);
		Assert.Equal(1, sent);
	}

	[Fact]
	public async Task NotifyUsersDenied_ReachesNobody()
	{
		using var app = new TestApplication();
		var one = await RoomFixture.PlayerAsync(app, "One");

		var (result, sent) = await new NotifyUsersHandler(app.Server)
			.Handle(new NotifyUsers(await PlainAsync(app), "hello", [one.Record.Id]), Token);

		Assert.Equal(ServerOutcome.Denied, result);
		Assert.Equal(0, sent);
	}

	private static async Task<Connection> AdminAsync(TestApplication app)
	{
		return await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPrivilegesExtensions.All));
	}

	private static async Task<Connection> PlainAsync(TestApplication app)
	{
		return await app.LoginAsync(await app.CreateUserAsync("Plain", "password", UserPrivileges.None));
	}

	/// <summary>A FAQ storage that keeps its entries in memory.</summary>
	private sealed class InMemoryFaqs : IFaqStorage
	{
		public Dictionary<string, string> Entries { get; } = [];

		public Task<IEnumerable<string>> ListAsync(CancellationToken ct = default)
		{
			return Task.FromResult<IEnumerable<string>>(Entries.Keys.ToList());
		}

		public Task<IFaqEntryWriter> CreateOrUpdateAsync(string entry)
		{
			return Task.FromResult<IFaqEntryWriter>(new Writer(Entries, entry));
		}

		public Task<StreamReader?> ReadAsync(string entry, CancellationToken ct = default)
		{
			return Task.FromResult<StreamReader?>(Entries.TryGetValue(entry, out var text)
				? new StreamReader(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)))
				: null);
		}

		public Task DeleteAsync(string entry)
		{
			Entries.Remove(entry);
			return Task.CompletedTask;
		}

		private sealed class Writer(Dictionary<string, string> entries, string entry) : IFaqEntryWriter
		{
			private string _text = string.Empty;

			public Task WriteAsync(string text)
			{
				_text = text;
				return Task.CompletedTask;
			}

			public void Commit()
			{
				entries[entry] = _text;
			}

			public void Dispose()
			{
			}

			public ValueTask DisposeAsync()
			{
				return ValueTask.CompletedTask;
			}
		}
	}
}
