using Basil.Application.Rulesets.Objects;
using Basil.Application.Rulesets.Objects.Events;
using Basil.Application.Rulesets.Services;
using Basil.Application.Shared.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Rulesets;

public static class DependencyInjection
{
	public static IServiceCollection AddRulesetsApplication(this IServiceCollection services)
	{
		services.AddSingleton<RulesetLibrary>(sp => new RulesetLibrary(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IRulesetUpdater>(),
			sp.GetRequiredService<IEventPublisher<RulesetEvent>>()));
		return services;
	}
}