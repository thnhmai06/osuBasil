using Basil.Application.Beatmaps;
using Basil.Application.Chat;
using Basil.Application.Multiplayer;
using Basil.Application.Scores;
using Basil.Application.Sessions;
using Basil.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application;

/// <summary>Registers the objects that make up Basil's application environment.</summary>
public static class DependencyInjection
{
	/// <summary>
	///     Adds the environment objects (the user registry, the general channel registry, the lobby) and
	///     the operations that use the repository and storage ports, which the caller registers.
	/// </summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddApplication(this IServiceCollection services)
	{
		services.AddSingleton<Gateway>();
		services.AddSingleton<UserRegistry>();
		services.AddSingleton<GeneralChannelRegistry>();
		services.AddSingleton<Lobby>();
		services.AddSingleton<ScoreSubmission>();
		services.AddSingleton<BeatmapCatalog>();
		services.AddSingleton<Registration>();

		return services;
	}
}