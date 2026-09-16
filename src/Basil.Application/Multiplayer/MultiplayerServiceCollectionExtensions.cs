using Basil.Application.Multiplayer.Handlers.Countdown;
using Basil.Application.Multiplayer.Handlers.Lifecycle;
using Basil.Application.Multiplayer.Handlers.Slots;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Multiplayer;

/// <summary>Registers the Multiplayer slice's Application-layer services.</summary>
public static class MultiplayerServiceCollectionExtensions
{
	/// <summary>Registers the Multiplayer slice's Application-layer services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddMultiplayerApplication(this IServiceCollection services)
	{
		services.AddSingleton<MatchBroadcast>();
		services.AddSingleton<MatchMembership>();
		services.AddSingleton<MatchLifecycle>();
		services.AddSingleton<MatchControlService>();
		services.AddSingleton<SetTeamHandler>();
		services.AddSingleton<SetSlotsHandler>();
		services.AddSingleton<TimerHandler>();
		services.AddSingleton<AbortTimerHandler>();
		services.AddSingleton<StartHandler>();
		services.AddSingleton<AbortHandler>();
		services.AddSingleton<CloseHandler>();
		services.AddSingleton<MatchReportService>();
		services.AddSingleton<MatchRecoveryService>();
		services.AddSingleton<IMpCommandService, MpCommandService>();
		services.AddSingleton<IPlayerLogoutHandler, MatchLeaveLogoutHandler>();

		services.AddSingleton<IMatchRegistry, InMemoryMatchRegistry>();

		return services;
	}
}
