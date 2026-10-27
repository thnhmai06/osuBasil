using Basil.Application.Content.Persistence;
using Basil.Domain.Content;
using Basil.Testing.Persistence;
using Npgsql;

namespace Basil.Infrastructure.Content.Tests.Persistence;

/// <summary>Contract tests of the storing of the settings of the server.</summary>
[Collection("Persistence")]
public sealed class PostgresSettingsRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
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

		var reloaded = await ReloadAsync();

		Assert.Equal(settings, reloaded);
	}

	[Fact]
	public async Task The_settings_are_stored_when_storing_them_returns()
	{
		var settings = new ServerSettings { Motd = "Welcome to the server." };

		await Settings.CreateOrUpdateAsync(settings, TestContext.Current.CancellationToken);

		var rows = await StoredRows.ReadAsync(ConnectionString,
			"select name, value from settings where name = @name",
			new Dictionary<string, object?> { ["@name"] = "motd" }, TestContext.Current.CancellationToken);

		Assert.Equal("Welcome to the server.", Assert.Single(rows).Text("value"));
	}

	[Fact]
	public async Task Settings_the_database_refuses_leave_the_settings_unchanged()
	{
		var before = await Settings.GetAsync(TestContext.Current.CancellationToken);

		await Assert.ThrowsAnyAsync<Exception>(() => Settings.CreateOrUpdateAsync(
			new ServerSettings { Motd = "A message with a lone surrogate \uD800." },
			TestContext.Current.CancellationToken));

		Assert.Null((await Settings.GetAsync(TestContext.Current.CancellationToken)).Motd);
		Assert.Same(before, await Settings.GetAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Of_two_concurrent_changes_of_the_settings_memory_holds_the_ones_stored_last()
	{
		var changes = new[]
		{
			Settings.CreateOrUpdateAsync(new ServerSettings { Motd = "First message." },
				TestContext.Current.CancellationToken),
			Settings.CreateOrUpdateAsync(new ServerSettings { Motd = "Second message." },
				TestContext.Current.CancellationToken)
		};
		await Task.WhenAll(changes);

		var rows = await StoredRows.ReadAsync(ConnectionString, "select value from settings where name = @name",
			new Dictionary<string, object?> { ["@name"] = "motd" }, TestContext.Current.CancellationToken);
		Assert.Equal(Assert.Single(rows).Text("value"),
			(await Settings.GetAsync(TestContext.Current.CancellationToken)).Motd);
	}

	[Theory]
	[InlineData("locked_creation", "not a number")]
	[InlineData("menu_icon_url", "not an address")]
	[InlineData("menu_icon_url", "relative/path")]
	[InlineData("mirror_download_endpoint", "::")]
	[InlineData("mirror_search_endpoint", "not an address")]
	[InlineData("menu_icon_image", "../outside.png")]
	public async Task A_stored_value_that_is_not_valid_leaves_the_setting_at_its_default(string name, string value)
	{
		await using (var connection = new NpgsqlConnection(ConnectionString))
		{
			await connection.OpenAsync(TestContext.Current.CancellationToken);
			await using var command =
				new NpgsqlCommand("insert into settings (name, value) values (@name, @value)", connection);
			command.Parameters.AddWithValue("@name", name);
			command.Parameters.AddWithValue("@value", value);
			await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
		}

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