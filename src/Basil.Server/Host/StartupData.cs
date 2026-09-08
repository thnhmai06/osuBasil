using Basil.Domain.Channels;
using Basil.Server.Features.Auth;
using Basil.Server.Features.Beatmaps;
using Basil.Server.Features.Bot;
using Basil.Server.Features.Chat;
using Basil.Server.Features.Content;
using Basil.Server.Features.Irc;
using Basil.Server.Features.Multiplayer;
using Basil.Server.Shared.Configuration;
using Basil.Server.Shared.Persistence;
using Microsoft.Extensions.Options;

namespace Basil.Server.Host;

/// <summary>Initializes runtime data for the host on startup.</summary>
internal static class StartupData
{
	/// <summary>
	///     Creates the storage folders, runs database migrations, and bootstraps runtime data on startup.
	/// </summary>
	/// <remarks>
	///     When no database path is configured, migration, channel fetch, beatmap ingestion, and bot
	///     bootstrap are skipped rather than failing startup; the channel registry is still always
	///     seeded, with an empty list in that case.
	/// </remarks>
	/// <param name="app">The built application whose scoped services perform the initialization.</param>
	public static async Task InitializeAsync(WebApplication app)
	{
		using var scope = app.Services.CreateScope();
		var logger = scope.ServiceProvider.GetRequiredService<ILogger<Bootstrap>>();

		// Forces MpReplies/IrcReplies to fully resolve every member from ReplyLocale's localization
		// files now, at boot, rather than lazily on whichever member a live chat command or IRC reply
		// first happens to touch -- a missing key is a startup failure, not one discovered mid-tournament.
		_ = MpReplies.CreateFailed;
		_ = IrcReplies.Welcome;
		logger.LogInformation("Reply locale loaded");

		await MigrateLegacyMenuDataAsync(scope.ServiceProvider, logger);

		var dbOptions = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
		var hasDatabase = !string.IsNullOrEmpty(dbOptions.Path);

		var storageOptions = scope.ServiceProvider.GetRequiredService<IOptions<StorageOptions>>().Value;
		foreach (var path in new[]
		         {
			         storageOptions.ReplaysPath, storageOptions.AvatarsPath, storageOptions.BeatmapsetsPath,
			         storageOptions.MenuSeasonalsPath, storageOptions.FaqsPath
		         })
			Directory.CreateDirectory(path);
		logger.LogInformation("Storage folders ready");

		var channelRepository = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
		var channelRegistry = scope.ServiceProvider.GetRequiredService<IChannelRegistry>();
		IReadOnlyList<Channel> allChannels = [];

		if (hasDatabase)
		{
			var connectionString = dbOptions.Build();
			Directory.CreateDirectory(Path.GetDirectoryName(dbOptions.ResolvePath())!);
			logger.LogInformation("Running database migrations");
			SqlMigrationRunner.RunMigrations(connectionString);
			logger.LogInformation("Database migrations complete");

			allChannels = await channelRepository.FetchAllAsync();
		}

		// Reads/writes the Settings table, so this must run after migrations create it when a real
		// database is in play -- moving it earlier (alongside the MirrorOptions-only version this
		// replaced) broke a from-scratch database, including a fresh Docker image build's build-time
		// OpenAPI doc generation, which runs Bootstrap.Main against an empty database before any
		// container ever starts. Deliberately NOT nested inside `if (hasDatabase)`: a test host with no
		// real database still registers a working (in-memory) ISettingsRepository and expects the
		// mirror it configured to seed and take effect.
		var mirrorService = scope.ServiceProvider.GetRequiredService<MirrorService>();
		await mirrorService.SeedFromConfigIfUnsetAsync();
		var mirror = await mirrorService.GetAsync();
		if (mirror.IsOnlineMode)
			logger.LogInformation("Beatmap serving mode: ONLINE — a mirror download endpoint is set. " +
			                      "Thumbnails/previews redirect to b.ppy.sh, downloads redirect to the configured " +
			                      "mirror, local-only asset routes report 503, all when missing locally.");
		else
			logger.LogInformation("Beatmap serving mode: OFFLINE — no mirror download endpoint is set. " +
			                      "All beatmap assets are served from local storage only.");

		channelRegistry.Seed(allChannels);

		if (hasDatabase)
		{
			var ingestionService = scope.ServiceProvider.GetRequiredService<BeatmapIngestionService>();
			await ingestionService.ReconcileAllAsync();

			var botBootstrap = scope.ServiceProvider.GetRequiredService<BotBootstrapService>();
			await botBootstrap.BootstrapAsync();

			var recoveryService = scope.ServiceProvider.GetRequiredService<MatchRecoveryService>();
			await recoveryService.RecoverAsync();

			var adminKeyService = scope.ServiceProvider.GetRequiredService<AdminKeyService>();
			if (await adminKeyService.IsBypassAsync())
				logger.LogWarning(
					"NO ADMIN KEY IS CONFIGURED — THE SERVER IS RUNNING IN BYPASS MODE. " +
					"ALL MANAGEMENT ACTIONS AND IN-GAME REGISTRATIONS ARE ACCEPTED WITHOUT AUTHENTICATION. " +
					"THIS IS INSECURE AND SHOULD ONLY BE USED FOR DEVELOPMENT. " +
					"Configure an admin key immediately via PUT /settings/adminkey.");
		}
	}

	/// <summary>
	///     Moves data from the pre-`Data/Menu/` folder layout into place, one time, on startup.
	/// </summary>
	/// <remarks>
	///     Idempotent: each move is guarded by the old path existing and the new one not existing yet,
	///     so a server that has already migrated (or was never on the old layout) does nothing.
	///     <c>MenuIcon:Path</c> is rewritten only when it still points at the exact file being moved --
	///     it defaults to an external URL, which this leaves untouched.
	/// </remarks>
	/// <param name="services">The scoped service provider used to update <c>MenuIcon:Path</c>.</param>
	/// <param name="logger">The logger startup messages are written to.</param>
	private static async Task MigrateLegacyMenuDataAsync(IServiceProvider services, ILogger logger)
	{
		var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
		var menuDir = Path.Combine(dataDir, "Menu");
		Directory.CreateDirectory(menuDir);

		var oldSeasonals = Path.Combine(dataDir, "Seasonals");
		var newSeasonals = Path.Combine(menuDir, "Seasonals");
		if (Directory.Exists(oldSeasonals) && !Directory.Exists(newSeasonals))
		{
			Directory.Move(oldSeasonals, newSeasonals);
			logger.LogInformation("Migrated legacy Data/Seasonals/ to Data/Menu/Seasonals/");
		}

		var oldIcon = Directory.Exists(dataDir)
			? Directory.EnumerateFiles(dataDir, "MenuIcon.*").FirstOrDefault()
			: null;
		if (oldIcon is null) return;

		var newIcon = Path.Combine(menuDir, $"Icon{Path.GetExtension(oldIcon)}");
		if (File.Exists(newIcon)) return;

		File.Move(oldIcon, newIcon);
		var settings = services.GetRequiredService<ISettingsRepository>();
		if (await settings.GetAsync("MenuIcon:Path") == oldIcon)
			await settings.SetAsync("MenuIcon:Path", newIcon);
		logger.LogInformation("Migrated legacy {Old} to {New}", oldIcon, newIcon);
	}
}
