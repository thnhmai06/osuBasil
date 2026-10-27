using Basil.Application.Persistence.Auth;
using Basil.Application.Persistence.Beatmaps;
using Basil.Application.Persistence.Chat;
using Basil.Application.Persistence.Content;
using Basil.Application.Persistence.Multiplayer;
using Basil.Application.Persistence.Scores;
using Basil.Application.Persistence.Users;
using Basil.Common.Infrastructure;
using Basil.Infrastructure.Persistence.Auth;
using Basil.Infrastructure.Persistence.Beatmaps;
using Basil.Infrastructure.Persistence.Chat;
using Basil.Infrastructure.Persistence.Content;
using Basil.Infrastructure.Persistence.Multiplayer;
using Basil.Common.Infrastructure.Postgres;
using Basil.Infrastructure.Persistence.Scores;
using Basil.Infrastructure.Persistence.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Persistence;

/// <summary>Registers the server's persistent storage: its data files and its database.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the data paths, the database and the migrations applied at startup.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddInfrastructurePersistence(this IServiceCollection services)
	{
		services.AddCommonInfrastructure();
		services.AddSingleton<IModuleMigration, ModuleMigration<UsersDbContext>>();
		services.AddSingleton<IModuleMigration, ModuleMigration<AuthDbContext>>();
		services.AddSingleton<IModuleMigration, ModuleMigration<BeatmapsDbContext>>();
		services.AddSingleton<IModuleMigration, ModuleMigration<ContentDbContext>>();
		services.AddSingleton<IModuleMigration, ModuleMigration<ScoresDbContext>>();
		services.AddSingleton<IModuleMigration, ModuleMigration<MultiplayerDbContext>>();
		services.AddSingleton<IModuleMigration, ModuleMigration<ChatDbContext>>();
		services.AddHostedService<StorageStartup>();
		services.AddSingleton<IUserRepository, PostgresUserRepository>();
		services.AddSingleton<ICredentialRepository, PostgresCredentialRepository>();
		services.AddSingleton<ILoginRepository, PostgresLoginRepository>();
		services.AddSingleton<IRestrictionRepository, PostgresRestrictionRepository>();
		services.AddSingleton<IRelationshipRepository, PostgresRelationshipRepository>();
		services.AddSingleton<IModerationLogRepository, PostgresModerationLogRepository>();
		services.AddSingleton<IUserAvatarStorage, FileUserAvatarStorage>();

		services.AddSingleton<PostgresChannelRepository>();
		services.AddSingleton<IChannelRepository>(provider => provider.GetRequiredService<PostgresChannelRepository>());
		services.AddSingleton<IResident>(provider => provider.GetRequiredService<PostgresChannelRepository>());

		services.AddSingleton<PostgresSettingsRepository>();
		services.AddSingleton<ISettingsRepository>(provider =>
			provider.GetRequiredService<PostgresSettingsRepository>());
		services.AddSingleton<IResident>(provider => provider.GetRequiredService<PostgresSettingsRepository>());
		services.AddSingleton<PostgresMenuBannerRepository>();
		services.AddSingleton<IMenuBannerRepository>(provider =>
			provider.GetRequiredService<PostgresMenuBannerRepository>());
		services.AddSingleton<IResident>(provider => provider.GetRequiredService<PostgresMenuBannerRepository>());

		services.AddSingleton<IMenuBannerStorage, FileMenuBannerStorage>();
		services.AddSingleton<IMenuIconStorage, FileMenuIconStorage>();
		services.AddSingleton<IMenuSeasonalsStorage, FileMenuSeasonalsStorage>();
		services.AddSingleton<IFaqStorage, FileFaqStorage>();
		services.AddSingleton<IScreenshotStorage, FileScreenshotStorage>();

		services.AddSingleton<IMatchRepository, PostgresMatchRepository>();
		services.AddSingleton<IMatchReportRepository, MatchReportRepository>();
		services.AddSingleton<IRoundRepository, PostgresRoundRepository>();
		services.AddSingleton<IRoundScoreRepository, PostgresRoundScoreRepository>();
		services.AddSingleton<IMatchEventRepository, PostgresMatchEventRepository>();

		services.AddSingleton<IScoreRepository, PostgresScoreRepository>();
		services.AddSingleton<IUserStatsRepository, PostgresUserStatsRepository>();
		services.AddSingleton<IReplayStorage, FileReplayStorage>();

		services.AddSingleton<IBeatmapsetRepository, PostgresBeatmapsetRepository>();
		services.AddSingleton<PostgresBeatmapRepository>();
		services.AddSingleton<IBeatmapRepository>(provider => provider.GetRequiredService<PostgresBeatmapRepository>());
		// The beatmapset repository needs the difficulty repository only once it stores, which breaks their cycle.
		services.AddSingleton(provider =>
			new Lazy<PostgresBeatmapRepository>(provider.GetRequiredService<PostgresBeatmapRepository>));
		services.AddSingleton<FileBeatmapsetStorage>();
		services.AddSingleton<IBeatmapsetStorage>(provider => provider.GetRequiredService<FileBeatmapsetStorage>());
		return services;
	}
}