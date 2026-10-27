using Basil.Application.Common.Events;
using Basil.Application.Scores.Capabilities;
using Basil.Application.Scores.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Scores;

/// <summary>Registers the application behavior and runtime objects owned by Scores.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the Scores application services and runtime objects.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddScoresApplication(this IServiceCollection services)
	{
		services.AddSingleton<ScoreRecorder>();
		services.AddSingleton<ScoreSubmitter>(sp => new ScoreSubmitter(
			sp.GetRequiredService<ScoreRecorder>(),
			sp.GetRequiredService<IEventPublisher<ScoreEvent>>(),
			sp.GetRequiredService<IScoreBeatmapAcceptance>(),
			sp.GetRequiredService<TimeProvider>()));
		return services;
	}
}