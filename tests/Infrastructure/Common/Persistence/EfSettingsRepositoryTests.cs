using Basil.Application.Common.Settings;
using Basil.Domain.Content;
using Basil.Domain.Users;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Common.Tests.Persistence;

/// <summary>Contract tests of the storing of the settings of the server.</summary>
[Collection("Persistence")]
public sealed class EfSettingsRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private ISettingsRepository Settings => Get<ISettingsRepository>();

	[Fact]
	public async Task NothingStored_SettingsDefault()
	{
		var settings = await Settings.GetAsync(TestContext.Current.CancellationToken);

		Assert.Equal(new ServerSettings(), settings);
	}

	[Fact]
	public async Task FreshServer_ReadsStoredSettings()
	{
		var settings = new ServerSettings
		{
			General = new ServerSettings.GeneralSettings { Motd = "Welcome to the server." },
			Locks = Locks.Accounts | Locks.Rooms | Locks.Profiles,
			Accounts = new ServerSettings.AccountSettings { DefaultPrivileges = UserPrivileges.Player },
			Sessions = new ServerSettings.SessionSettings
			{
				ApiToken = new ServerSettings.ApiTokenSettings { Lifetime = TimeSpan.FromDays(3) }
			},
			Multiplayer = new ServerSettings.MultiplayerSettings
			{
				Rounds = new ServerSettings.RoundSettings { ScoreWait = TimeSpan.FromSeconds(4) }
			},
			Bot = new ServerSettings.BotSettings { CommandPrefix = "?" },
			RateLimits = new ServerSettings.RateLimitSettings
			{
				Chat = new ServerSettings.Limit(3, TimeSpan.FromSeconds(2)),
				Admission = new ServerSettings.AdmissionLimitSettings(7, TimeSpan.FromSeconds(30), 3, 11, 12, 13, 14,
					0.2, 0.4, 1, TimeSpan.FromSeconds(9))
			},
			Uploads = new ServerSettings.UploadSettings { AvatarMaxSide = 512 },
			Cors = new ServerSettings.CorsSettings { AllowedOrigins = ["https://example.com"] },
			MenuIcon = new ServerSettings.MenuIconSettings
			{
				Url = new Uri("https://example.com/icon"),
				Image = new ContentReference("menu/icon.png")
			},
			Beatmaps = new ServerSettings.BeatmapSettings
			{
				Mirror = new ServerSettings.BeatmapMirrorSettings
				{
					DownloadEndpoint = new Uri("https://example.com/download"),
					SearchEndpoint = new Uri("https://example.com/search")
				}
			}
		};
		await Settings.CreateOrUpdateAsync(settings);

		var reloaded = await ReloadAsync();

		Assert.Equal(settings, reloaded);
	}

	[Fact]
	public async Task StoredSettings_KeepUnsetValues()
	{
		var settings = new ServerSettings
		{
			General = new ServerSettings.GeneralSettings { Motd = null },
			MenuIcon = new ServerSettings.MenuIconSettings { Url = null, Image = null },
			Beatmaps = new ServerSettings.BeatmapSettings
			{
				Mirror = new ServerSettings.BeatmapMirrorSettings { DownloadEndpoint = null, SearchEndpoint = null }
			}
		};
		await Settings.CreateOrUpdateAsync(settings);

		var reloaded = await ReloadAsync();

		Assert.Equal(settings, reloaded);
	}

	[Fact]
	public async Task StoredSettings_PersistedOnReturn()
	{
		var settings = new ServerSettings
			{ General = new ServerSettings.GeneralSettings { Motd = "Welcome to the server." } };

		await Settings.CreateOrUpdateAsync(settings);

		var rows = await StoredRows.ReadAsync(ConnectionString,
			"select name, value from settings where name = @name",
			new Dictionary<string, object?> { ["@name"] = "general" }, TestContext.Current.CancellationToken);

		Assert.Contains("Welcome to the server.", Assert.Single(rows).Text("value"));
	}

	[Fact]
	public async Task RefusedSettings_LeftUnchanged()
	{
		var before = await Settings.GetAsync(TestContext.Current.CancellationToken);
		await StoredRows.ExecuteAsync(ConnectionString, "drop table settings", null,
			TestContext.Current.CancellationToken);

		await Assert.ThrowsAnyAsync<Exception>(() => Settings.CreateOrUpdateAsync(
			new ServerSettings
			{
				General = new ServerSettings.GeneralSettings { Motd = "A message the database cannot store." }
			}));

		Assert.Null((await Settings.GetAsync(TestContext.Current.CancellationToken)).General.Motd);
		Assert.Same(before, await Settings.GetAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task ConcurrentSettingChanges_LastStoredWins()
	{
		var changes = new[]
		{
			Settings.CreateOrUpdateAsync(
				new ServerSettings { General = new ServerSettings.GeneralSettings { Motd = "First message." } }),
			Settings.CreateOrUpdateAsync(
				new ServerSettings { General = new ServerSettings.GeneralSettings { Motd = "Second message." } })
		};
		await Task.WhenAll(changes);

		var rows = await StoredRows.ReadAsync(ConnectionString, "select value from settings where name = @name",
			new Dictionary<string, object?> { ["@name"] = "general" }, TestContext.Current.CancellationToken);
		Assert.Contains((await Settings.GetAsync(TestContext.Current.CancellationToken)).General.Motd!,
			Assert.Single(rows).Text("value"));
	}

	[Theory]
	[InlineData("locks", "\"not a flag\"")]
	[InlineData("menu_icon", "{\"Url\":\"relative/path\"}")]
	[InlineData("menu_icon", "{\"Image\":\"../outside.png\"}")]
	[InlineData("beatmaps", "{\"Mirror\":{\"DownloadEndpoint\":\"::\"}}")]
	[InlineData("sessions", "not json")]
	[InlineData("multiplayer", "{\"Rounds\":{\"ScoreWait\":\"00:00:00\"}}")]
	[InlineData("rate_limits", "{\"Chat\":{\"Count\":0,\"Window\":\"00:00:05\"}}")]
	public async Task InvalidStoredCategory_KeepsDefaults(string name, string value)
	{
		await StoredRows.InsertAsync(ConnectionString, "settings", [
			new Dictionary<string, object?> { ["name"] = name, ["value"] = value }
		], TestContext.Current.CancellationToken);

		var reloaded = await ReloadAsync();

		Assert.Equal(new ServerSettings(), reloaded);
	}

	/// <summary>Reads the settings as a fresh server would read them.</summary>
	/// <returns>The settings the fresh server loaded from the database.</returns>
	private async Task<ServerSettings> ReloadAsync()
	{
		await using var fresh = new StorageComposition(ConnectionString);
		var repository = fresh.Get<ISettingsRepository>();
		await fresh.ResidentOf(repository).LoadAsync(TestContext.Current.CancellationToken);
		return await repository.GetAsync(TestContext.Current.CancellationToken);
	}
}