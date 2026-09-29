using Basil.Application.Contracts.Events;
using Basil.Application.Models.Events;
using Basil.Application.Models.Events.Multiplayer;
using Basil.Application.Models.Sessions;
using Basil.Application.Services.EventHandlers;
using Basil.Application.Services.Operations;
using Basil.Application.Services.Operations.Commands.Mp;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Services;

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
		services.AddSingleton<Lobby>();
		services.AddSingleton<Messaging>();
		services.AddSingleton<RoomCountdowns>();
		services.AddSingleton<ScoreSubmission>();
		services.AddSingleton<BeatmapCatalog>();

		services.AddSingleton<RoomLifecycleCommands>();
		services.AddSingleton<RoomSettingsCommands>();
		services.AddSingleton<SlotCommands>();
		services.AddSingleton<RefereeCommands>();
		services.AddSingleton<MatchFlowCommands>();
		services.AddSingleton<MpCommands>();
		services.AddSingleton<ChatCommands>();

		services.AddSingleton<RoomEventHandlers>();
		services.AddSingleton<IEventHandler<RoomSettingsEvent>>(sp => sp.GetRequiredService<RoomEventHandlers>());
		services.AddSingleton<IEventHandler<RoomSlotsEvent>>(sp => sp.GetRequiredService<RoomEventHandlers>());
		services.AddSingleton<IEventHandler<RoomMembershipEvent>>(sp => sp.GetRequiredService<RoomEventHandlers>());
		services.AddSingleton<IEventHandler<RoomAuthorityEvent>>(sp => sp.GetRequiredService<RoomEventHandlers>());
		services.AddSingleton<IEventHandler<RoomAccessEvent>>(sp => sp.GetRequiredService<RoomEventHandlers>());
		services.AddSingleton<IEventHandler<RoomClosed>>(sp => sp.GetRequiredService<RoomEventHandlers>());

		services.AddSingleton<RoundEventHandlers>();
		services.AddSingleton<IEventHandler<RoundEvent>>(sp => sp.GetRequiredService<RoundEventHandlers>());

		services.AddSingleton<SessionEventHandlers>();
		services.AddSingleton<IEventHandler<PresenceEvent>>(sp => sp.GetRequiredService<SessionEventHandlers>());
		services.AddSingleton<IEventHandler<SpectatorEvent>>(sp => sp.GetRequiredService<SessionEventHandlers>());

		services.AddSingleton<ChannelEventHandlers>();
		services.AddSingleton<IEventHandler<ChannelMembershipEvent>>(sp =>
			sp.GetRequiredService<ChannelEventHandlers>());

		return services;
	}
}