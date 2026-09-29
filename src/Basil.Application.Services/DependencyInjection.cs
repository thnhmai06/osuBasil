using Basil.Application.Contracts.Events;
using Basil.Application.Models.Multiplayer;
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

		services.AddSingleton<RoomMembershipEventHandlers>();
		services.AddSingleton<IEventHandler<PlayerJoined>>(sp =>
			sp.GetRequiredService<RoomMembershipEventHandlers>());
		services.AddSingleton<IEventHandler<PlayerLeft>>(sp =>
			sp.GetRequiredService<RoomMembershipEventHandlers>());
		services.AddSingleton<IEventHandler<PlayerKicked>>(sp =>
			sp.GetRequiredService<RoomMembershipEventHandlers>());
		services.AddSingleton<IEventHandler<PlayerBanned>>(sp =>
			sp.GetRequiredService<RoomMembershipEventHandlers>());
		services.AddSingleton<IEventHandler<PlayerInvited>>(sp =>
			sp.GetRequiredService<RoomMembershipEventHandlers>());
		services.AddSingleton<IEventHandler<HostChanged>>(sp =>
			sp.GetRequiredService<RoomMembershipEventHandlers>());
		services.AddSingleton<IEventHandler<RefereeAdded>>(sp =>
			sp.GetRequiredService<RoomMembershipEventHandlers>());
		services.AddSingleton<IEventHandler<RefereeRemoved>>(sp =>
			sp.GetRequiredService<RoomMembershipEventHandlers>());

		services.AddSingleton<RoomMatchEventHandlers>();
		services.AddSingleton<IEventHandler<SlotChanged>>(sp => sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IEventHandler<SlotLocked>>(sp => sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IEventHandler<SettingsChanged>>(sp =>
			sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IEventHandler<RoomLockChanged>>(sp =>
			sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IEventHandler<RoundStarted>>(sp => sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IEventHandler<PlayerLoaded>>(sp => sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IEventHandler<AllPlayersLoaded>>(sp =>
			sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IEventHandler<PlayerSkipped>>(sp =>
			sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IEventHandler<AllPlayersSkipped>>(sp =>
			sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IEventHandler<PlayerFailed>>(sp => sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IEventHandler<PlayerCompleted>>(sp =>
			sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IEventHandler<RoundEnded>>(sp => sp.GetRequiredService<RoomMatchEventHandlers>());

		services.AddSingleton<SessionEventHandlers>();
		services.AddSingleton<IEventHandler<StatusChanged>>(sp => sp.GetRequiredService<SessionEventHandlers>());
		services.AddSingleton<IEventHandler<SpectateStarted>>(sp =>
			sp.GetRequiredService<SessionEventHandlers>());
		services.AddSingleton<IEventHandler<SpectateStopped>>(sp =>
			sp.GetRequiredService<SessionEventHandlers>());

		services.AddSingleton<ChannelEventHandlers>();
		services.AddSingleton<IEventHandler<ChannelJoined>>(sp => sp.GetRequiredService<ChannelEventHandlers>());
		services.AddSingleton<IEventHandler<ChannelParted>>(sp => sp.GetRequiredService<ChannelEventHandlers>());

		return services;
	}
}