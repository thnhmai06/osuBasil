using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Chat;
using Basil.Application.Storage.Contracts.Content;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Scores;
using Basil.Application.Storage.Contracts.Users;
using Basil.Infrastructure.Storage.Beatmaps;
using Basil.Infrastructure.Storage.Caching;
using Basil.Infrastructure.Storage.Chat;
using Basil.Infrastructure.Storage.Content;
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
		services.AddSingleton<DatabaseMigrator>();
		services.AddHostedService<StorageStartup>();
		services.TryAddSingleton(TimeProvider.System);
		services.AddSingleton<WriteBuffer>();
		services.AddHostedService<WriteFlusher>();

		services.AddSingleton<IUserRepository, SqliteUserRepository>();
		services.AddSingleton<ICredentialRepository, SqliteCredentialRepository>();
		services.AddSingleton<ILoginRepository, SqliteLoginRepository>();
		services.AddSingleton<IRestrictionRepository, SqliteRestrictionRepository>();
		services.AddSingleton<IRelationshipRepository, SqliteRelationshipRepository>();
		services.AddSingleton<IUserAvatarStorage, FileUserAvatarStorage>();

		services.AddSingleton<IChannelRepository, SqliteChannelRepository>();

		services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
		services.AddSingleton<IMenuBannerRepository, SqliteMenuBannerRepository>();
		services.AddSingleton<IMenuBannerStorage, FileMenuBannerStorage>();
		services.AddSingleton<IMenuIconStorage, FileMenuIconStorage>();
		services.AddSingleton<IMenuSeasonalsStorage, FileMenuSeasonalsStorage>();
		services.AddSingleton<IFaqStorage, FileFaqStorage>();

		services.AddSingleton<IMatchRepository, SqliteMatchRepository>();
		services.AddSingleton<MatchReportCache>();
		services.AddSingleton<IMatchReportRepository, MatchReportRepository>();
		services.AddSingleton<IRoundRepository, SqliteRoundRepository>();
		services.AddSingleton<IMatchEventRepository, SqliteMatchEventRepository>();

		services.AddSingleton<IScoreRepository, SqliteScoreRepository>();
		services.AddSingleton<IUserStatsRepository, SqliteUserStatsRepository>();
		services.AddSingleton<IReplayStorage, FileReplayStorage>();

		services.AddSingleton<IBeatmapsetRepository, SqliteBeatmapsetRepository>();
		services.AddSingleton<IBeatmapRepository, SqliteBeatmapRepository>();
		services.AddSingleton<IBeatmapsetStorage, FileBeatmapsetStorage>();
		return services;
	}
}
