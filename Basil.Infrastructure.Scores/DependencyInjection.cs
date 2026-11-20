using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Ports;
using Basil.Application.Scores.Services;
using Basil.Application.Scores.Storage;
using Basil.Infrastructure.Scores.Logic;
using Basil.Infrastructure.Scores.Persistence;
using Basil.Infrastructure.Scores.Services;
using Basil.Infrastructure.Scores.Storage;
using Basil.Infrastructure.Scores.Workers;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Scores;

public static class DependencyInjection
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddScoresInfrastructure()
		{
			services.AddSingleton<IModuleMigration, ModuleMigration<ScoresDbContext>>();
			services.AddSingleton<IScoreRepository, EfScoreRepository>();
			services.AddSingleton<IUserStatsRepository, EfUserStatsRepository>();
			services.AddSingleton<IReplayStorage, FileReplayStorage>();
			services.AddSingleton<IScorePerformanceRecalculator, ScorePerformanceRecalculator>();
			services.AddScoresLogic();
			return services;
		}

		public IServiceCollection AddScoresLogic()
		{
			services.AddSingleton<IScoreRecorder, ScoreRecorder>();
			return services;
		}

		public IServiceCollection AddScoresBackgroundWork()
		{
			services.AddHostedService<ScorePerformanceBackfill>();
			return services;
		}
	}
}