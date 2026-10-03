using Basil.Application.Beatmaps;
using Basil.Application.Chat;
using Basil.Application.Contracts.Chat;
using Basil.Application.Contracts.Sessions;
using Basil.Application.Contracts.Users;
using Basil.Application.Multiplayer;
using Basil.Application.Scores;
using Basil.Application.Services.Chat;
using Basil.Application.Services.Sessions;
using Basil.Application.Services.Users;
using Basil.Application.Sessions;
using Basil.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application;

/// <summary>Registers the objects that make up Basil's application environment.</summary>
public static class DependencyInjection
{
	/// <summary>
	///     Adds the environment objects (the user registry, the general channel registry, the lobby) and
	///     the services that use the repository and storage ports, which the caller registers.
	/// </summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddApplication(this IServiceCollection services)
	{
		services.AddSingleton<UserRegistry>();
		services.AddSingleton<GeneralChannelRegistry>();
		services.AddSingleton<Lobby>();
		services.AddSingleton<ScoreSubmission>();
		services.AddSingleton<BeatmapCatalog>();

		services.AddSingleton<SessionService>();
		services.AddSingleton<ISessionService>(sp => sp.GetRequiredService<SessionService>());

		services.AddSingleton<AuthService>();
		services.AddSingleton<IAuthService>(sp => sp.GetRequiredService<AuthService>());

		services.AddSingleton<UserService>();
		services.AddSingleton<IUserService>(sp => sp.GetRequiredService<UserService>());

		services.AddSingleton<ChannelService>();
		services.AddSingleton<IChannelService>(sp => sp.GetRequiredService<ChannelService>());

		return services;
	}
}