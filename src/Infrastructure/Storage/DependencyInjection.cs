using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Chat;
using Basil.Application.Storage.Contracts.Content;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Scores;
using Basil.Application.Storage.Contracts.Users;
using Basil.Infrastructure.Storage.Beatmaps;
using Basil.Infrastructure.Storage.Chat;
using Basil.Infrastructure.Storage.Content;
using Basil.Infrastructure.Storage.Files;
using Basil.Infrastructure.Storage.Multiplayer;
using Basil.Infrastructure.Storage.Scores;
using Basil.Infrastructure.Storage.Users;
using Basil.Infrastructure.Storage.Writing;
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
		services.AddSingleton<FileMenuBannerStorage>();
		services.AddSingleton<IMenuBannerStorage>(provider => provider.GetRequiredService<FileMenuBannerStorage>());
		services.AddSingleton<IResident>(provider => provider.GetRequiredService<FileMenuBannerStorage>());
		services.AddSingleton<FileMenuIconStorage>();
		services.AddSingleton<IMenuIconStorage>(provider => provider.GetRequiredService<FileMenuIconStorage>());
		services.AddSingleton<IResident>(provider => provider.GetRequiredService<FileMenuIconStorage>());
		services.AddSingleton<FileMenuSeasonalsStorage>();
		services.AddSingleton<IMenuSeasonalsStorage>(provider =>
			provider.GetRequiredService<FileMenuSeasonalsStorage>());
		services.AddSingleton<IResident>(provider => provider.GetRequiredService<FileMenuSeasonalsStorage>());
		services.AddSingleton<FileFaqStorage>();
		services.AddSingleton<IFaqStorage>(provider => provider.GetRequiredService<FileFaqStorage>());
		services.AddSingleton<IResident>(provider => provider.GetRequiredService<FileFaqStorage>());

		services.AddSingleton<IMatchRepository, PostgresMatchRepository>();
		services.AddSingleton<IMatchReportRepository, MatchReportRepository>();
		services.AddSingleton<IRoundRepository, PostgresRoundRepository>();
		services.AddSingleton<IMatchEventRepository, PostgresMatchEventRepository>();

		services.AddSingleton<IScoreRepository, PostgresScoreRepository>();
		services.AddSingleton<IUserStatsRepository, PostgresUserStatsRepository>();
		services.AddSingleton<IReplayStorage, FileReplayStorage>();

		services.AddSingleton<IBeatmapsetRepository, PostgresBeatmapsetRepository>();
		services.AddSingleton<IBeatmapRepository, PostgresBeatmapRepository>();
		services.AddSingleton<FileBeatmapsetStorage>();
		services.AddSingleton<IBeatmapsetStorage>(provider => provider.GetRequiredService<FileBeatmapsetStorage>());
		services.AddHostedService<BeatmapsetFilesWatcher>();
		return services;
	}
}