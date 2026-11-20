using Basil.Application.Rulesets.Ports;
using Basil.Application.Rulesets.Services;
using Basil.Infrastructure.Rulesets.Adapters;
using Basil.Infrastructure.Rulesets.Services;
using Basil.Infrastructure.Rulesets.Storage;
using Basil.Infrastructure.Rulesets.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
			services.AddSingleton<IRulesetUpdater>(provider => new RulesetUpdater(
				provider.GetRequiredService<RulesetStore>(), new HttpClient { Timeout = TimeSpan.FromMinutes(5) },
				provider.GetRequiredService<ILogger<RulesetUpdater>>()));
			services.AddSingleton<CalculationQueue>();
			return services;
		}

		public IServiceCollection AddRulesetsBackgroundWork()
		{
			services.AddHostedService<CalculationWorker>();
			services.AddHostedService<RulesetsAnnouncer>();
			return services;
		}
	}
}