using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Scores;

/// <summary>Registers the Scores slice's Application-layer services.</summary>
public static class ScoresServiceCollectionExtensions
{
	/// <summary>Registers the Scores slice's Application-layer services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddScoresApplication(this IServiceCollection services)
	{
		services.AddSingleton<ScoreSubmissionService>();

		return services;
	}
}