using Basil.Application.Storage.Auth;
using Basil.Application.Storage.Beatmaps;
using Basil.Application.Storage.Chat;
using Basil.Application.Storage.Content;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Scores;
using Basil.Application.Storage.Users;
using Basil.Infrastructure.Storage.Auth;
using Basil.Infrastructure.Storage.Beatmaps;
using Basil.Infrastructure.Storage.Chat;
using Basil.Infrastructure.Storage.Common;
using Basil.Infrastructure.Storage.Content;
using Basil.Infrastructure.Storage.Postgres;
using Basil.Infrastructure.Storage.Files;
using Basil.Infrastructure.Storage.Multiplayer;
using Basil.Infrastructure.Storage.Scores;
using Basil.Infrastructure.Storage.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Infrastructure.Storage;

/// <summary>Registers the server's persistent storage: its data files and its database.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the data paths, the database and the migrations applied at startup.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddInfrastructureStorage(this IServiceCollection services)
	{
		services.AddSingleton<DataPaths>();
		services.AddSingleton<Database>();
		services.AddSingleton<DatabaseReader>();
		services.AddSingleton<DatabaseMigrator>();
		services.AddHostedService<StorageStartup>();
		services.TryAddSingleton(TimeProvider.System);
		services.AddSingleton<DatabaseWriter>();
		services.AddHostedService(provider => provider.GetRequiredService<DatabaseWriter>());

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
		services.AddSingleton<IBeatmapRepository, PostgresBeatmapRepository>();
		services.AddSingleton<FileBeatmapsetStorage>();
		services.AddSingleton<IBeatmapsetStorage>(provider => provider.GetRequiredService<FileBeatmapsetStorage>());
		return services;
	}
}