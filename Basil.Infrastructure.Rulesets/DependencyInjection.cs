using Basil.Application.Rulesets.Ports;
using Basil.Application.Rulesets.Services;
using Basil.Infrastructure.Rulesets.Adapters;
using Basil.Infrastructure.Rulesets.Services;
using Basil.Infrastructure.Rulesets.Storage;
using Basil.Infrastructure.Rulesets.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Rulesets;

public static class DependencyInjection
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddRulesetsInfrastructure()
		{
			services.AddSingleton<IBeatmapAnalyser, OsuBeatmapAnalyser>();
			services.AddSingleton<IBeatmapCalculator, OsuBeatmapCalculator>();
			services.AddSingleton<RulesetStore>();
			services.AddSingleton<AttributeStorage>();
			services.AddSingleton<IRulesetUpdater, RulesetUpdater>();
			services.AddSingleton<CalculationQueue>();
			return services;
		}

		public IServiceCollection AddRulesetsBackgroundWork()
		{
			services.AddHostedService<CalculationWorker>();
			return services;
		}
	}
}