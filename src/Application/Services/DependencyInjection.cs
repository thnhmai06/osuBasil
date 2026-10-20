using Basil.Application.Services.Beatmaps;
using Basil.Application.Services.Scores;
using Basil.Application.Services.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Services;

/// <summary>Registers the application services.</summary>
internal static class DependencyInjection
{
	/// <summary>Adds the application services, which use the repository, storage and capability ports.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	internal static IServiceCollection AddApplicationServices(this IServiceCollection services)
	{
		services.AddSingleton<AccountService>();
		services.AddSingleton<ScoreRecorder>();
		services.AddSingleton<DifficultyAnalyser>();
		services.AddSingleton<BeatmapsetMatcher>();
		services.AddSingleton<BeatmapsetPlanner>();
		services.AddSingleton<BeatmapsetFileWriter>();
		services.AddSingleton<BeatmapsetFileAudit>();
		services.AddSingleton<BeatmapsetStorer>();
		services.AddSingleton<BeatmapsetImporter>();
		services.AddSingleton<BeatmapsetScanner>();
		return services;
	}
}