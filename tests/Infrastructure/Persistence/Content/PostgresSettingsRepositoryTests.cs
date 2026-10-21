using Basil.Application.Persistence.Content;
using Basil.Domain;
using Basil.Domain.Content;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Content;

/// <summary>Contract tests of the storing of the settings of the server.</summary>
[Collection("Persistence")]
public sealed class PostgresSettingsRepositoryTests : DatabaseTests
{
	public PostgresSettingsRepositoryTests(PersistenceFixture container) : base(container) { }

	private ISettingsRepository Settings => Get<ISettingsRepository>();

	[Fact]
	public async Task The_settings_have_their_defaults_when_nothing_is_stored()
	{
		var settings = await Settings.GetAsync(TestContext.Current.CancellationToken);

		Assert.Equal(new ServerSettings(), settings);
	}

	[Fact]
	public async Task The_stored_settings_are_read_back_by_a_fresh_server()
	{
		var settings = new ServerSettings
		{
			Motd = "Welcome to the server.",
			LockedCreation = ServerSettings.CreationLocks.Accounts | ServerSettings.CreationLocks.Rooms,
			MenuIcon = new ServerSettings.MenuIconSettings
			{
				Url = new Uri("https://example.com/icon"),
				Image = new ContentReference("menu/icon.png")
			},
			BeatmapMirror = new ServerSettings.BeatmapMirrorSettings
			{
				DownloadEndpoint = new Uri("https://example.com/download"),
				SearchEndpoint = new Uri("https://example.com/search")
			}
		};
		await Settings.CreateOrUpdateAsync(settings, TestContext.Current.CancellationToken);
		await WaitForSettingAsync("motd", settings.Motd);

		var reloaded = await ReloadAsync();

		Assert.Equal(settings, reloaded);
	}

	[Fact]
	public async Task The_stored_settings_keep_their_unset_values()
	{
		var settings = new ServerSettings
		{
			Motd = null,
			MenuIcon = new ServerSettings.MenuIconSettings { Url = null, Image = null },
			BeatmapMirror = new ServerSettings.BeatmapMirrorSettings
			{
				DownloadEndpoint = null,
				SearchEndpoint = null
			}
		};
		await Settings.CreateOrUpdateAsync(settings, TestContext.Current.CancellationToken);
		await WaitForSettingAsync("motd", null);

		var reloaded = await ReloadAsync();

		Assert.Equal(settings, reloaded);
	}

	[Fact]
	public async Task The_settings_are_stored()
	{
		var settings = new ServerSettings { Motd = "Welcome to the server." };

		await Settings.CreateOrUpdateAsync(settings, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select name, value from settings where name = @name",
			new Dictionary<string, object?> { ["@name"] = "motd" },
			rows => rows.Count == 1 && rows[0].Text("value") == "Welcome to the server.",
			"the stored settings", TestContext.Current.CancellationToken);

		Assert.Single(rows);
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

	/// <summary>Waits until a setting is stored with the given value.</summary>
	/// <param name="name">The name of the setting.</param>
	/// <param name="value">The value the setting must hold.</param>
	private async Task WaitForSettingAsync(string name, string? value)
	{
		await StoredRows.WaitForAsync(ConnectionString, "select name, value from settings where name = @name",
			new Dictionary<string, object?> { ["@name"] = name },
			rows => rows.Count == 1 && rows[0].Text("value") == value,
			$"the stored setting {name}", TestContext.Current.CancellationToken);
	}
}
