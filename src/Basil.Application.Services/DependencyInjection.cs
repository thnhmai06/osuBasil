using Basil.Application.Contracts.Events;
using Basil.Application.Models.Sessions;
using Basil.Application.Services.EventHandlers;
using Basil.Application.Services.Operations;
using Basil.Application.Services.Operations.Commands.Mp;
using Basil.Domain.Chat;
using Basil.Domain.Multiplayer.Runtime;
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
		services.AddSingleton<IDomainEventHandler<PlayerJoined>>(sp =>
			sp.GetRequiredService<RoomMembershipEventHandlers>());
		services.AddSingleton<IDomainEventHandler<PlayerLeft>>(sp =>
			sp.GetRequiredService<RoomMembershipEventHandlers>());
		services.AddSingleton<IDomainEventHandler<PlayerKicked>>(sp =>
			sp.GetRequiredService<RoomMembershipEventHandlers>());
		services.AddSingleton<IDomainEventHandler<PlayerBanned>>(sp =>
			sp.GetRequiredService<RoomMembershipEventHandlers>());
		services.AddSingleton<IDomainEventHandler<PlayerInvited>>(sp =>
			sp.GetRequiredService<RoomMembershipEventHandlers>());
		services.AddSingleton<IDomainEventHandler<HostChanged>>(sp =>
			sp.GetRequiredService<RoomMembershipEventHandlers>());
		services.AddSingleton<IDomainEventHandler<RefereeAdded>>(sp =>
			sp.GetRequiredService<RoomMembershipEventHandlers>());
		services.AddSingleton<IDomainEventHandler<RefereeRemoved>>(sp =>
			sp.GetRequiredService<RoomMembershipEventHandlers>());

		services.AddSingleton<RoomMatchEventHandlers>();
		services.AddSingleton<IDomainEventHandler<SlotChanged>>(sp => sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IDomainEventHandler<SlotLocked>>(sp => sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IDomainEventHandler<SettingsChanged>>(sp =>
			sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IDomainEventHandler<RoomLockChanged>>(sp =>
			sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IDomainEventHandler<RoundStarted>>(sp => sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IDomainEventHandler<PlayerLoaded>>(sp => sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IDomainEventHandler<AllPlayersLoaded>>(sp =>
			sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IDomainEventHandler<PlayerSkipped>>(sp =>
			sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IDomainEventHandler<AllPlayersSkipped>>(sp =>
			sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IDomainEventHandler<PlayerFailed>>(sp => sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IDomainEventHandler<PlayerCompleted>>(sp =>
			sp.GetRequiredService<RoomMatchEventHandlers>());
		services.AddSingleton<IDomainEventHandler<RoundEnded>>(sp => sp.GetRequiredService<RoomMatchEventHandlers>());

		services.AddSingleton<SessionEventHandlers>();
		services.AddSingleton<IDomainEventHandler<StatusChanged>>(sp => sp.GetRequiredService<SessionEventHandlers>());
		services.AddSingleton<IDomainEventHandler<SpectateStarted>>(sp =>
			sp.GetRequiredService<SessionEventHandlers>());
		services.AddSingleton<IDomainEventHandler<SpectateStopped>>(sp =>
			sp.GetRequiredService<SessionEventHandlers>());

		services.AddSingleton<ChannelEventHandlers>();
		services.AddSingleton<IDomainEventHandler<ChannelJoined>>(sp => sp.GetRequiredService<ChannelEventHandlers>());
		services.AddSingleton<IDomainEventHandler<ChannelParted>>(sp => sp.GetRequiredService<ChannelEventHandlers>());
		services.AddSingleton<IDomainEventHandler<ChannelTopicChanged>>(sp =>
			sp.GetRequiredService<ChannelEventHandlers>());

		return services;
	}
}