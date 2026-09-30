using Microsoft.Extensions.DependencyInjection;
using Basil.Application.Beatmaps;
using Basil.Application.Chat;
using Basil.Application.Multiplayer;
using Basil.Application.Scores;
using Basil.Application.Sessions;

namespace Basil.Application;

/// <summary>Registers Basil's application-layer operations, chat commands, and domain event handlers.</summary>
public static class DependencyInjection
{
	/// <summary>
	///     Adds every concrete Application operation, chat command, and domain event handler, along
	///     with the Models and Contracts projects' own (currently empty) registrations.
	/// </summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddApplication(this IServiceCollection services)
	{
		services.AddSingleton<Gateway>();
		services.AddSingleton<Presence>();
		services.AddSingleton<ChatChannels>();
		services.AddSingleton<Lobby>();
		services.AddSingleton<RoomCountdowns>();
		services.AddSingleton<ScoreSubmission>();
		services.AddSingleton<BeatmapCatalog>();

		return services;
	}
}