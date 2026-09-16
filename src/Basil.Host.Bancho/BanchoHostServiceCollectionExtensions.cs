using Basil.Application.Chat;
using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Spectating;
using Basil.Host.Bancho.Chat.Packets;
using Basil.Host.Bancho.Multiplayer.Packets;
using Basil.Host.Bancho.Shared.Http;
using Basil.Host.Bancho.Shared.Sessions;
using Basil.Host.Bancho.Spectating.Packets;
using Basil.Host.Bancho.Users.Packets;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Bancho;

/// <summary>Registers the bancho packet-transport seam: handlers, notifiers, and the dispatcher.</summary>
public static class BanchoHostServiceCollectionExtensions
{
	/// <summary>
	///     Registers every <see cref="IPacketHandler" /> and packet-transport notifier into the given
	///     service collection, along with the <see cref="PacketDispatcher" /> that routes incoming
	///     packets to them.
	/// </summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddBanchoHost(this IServiceCollection services)
	{
		services.AddSingleton<IChatNotifier, ChatNotifier>();
		services.AddSingleton<IChannelNotifier, ChannelNotifier>();
		services.AddSingleton<IMatchNotifier, MatchNotifier>();
		services.AddSingleton<ISpectatorNotifier, SpectatorNotifier>();

		services.AddSingleton<IPacketHandler, ChannelJoinHandler>();
		services.AddSingleton<IPacketHandler, ChannelPartHandler>();
		services.AddSingleton<IPacketHandler, LobbyJoinHandler>();
		services.AddSingleton<IPacketHandler, LobbyPartHandler>();
		services.AddSingleton<IPacketHandler, SendPublicMessageHandler>();
		services.AddSingleton<IPacketHandler, SendPrivateMessageHandler>();
		services.AddSingleton<IPacketHandler, ToggleBlockNonFriendDmsHandler>();

		services.AddSingleton<IPacketHandler, CreateMatchHandler>();
		services.AddSingleton<IPacketHandler, JoinMatchHandler>();
		services.AddSingleton<IPacketHandler, PartMatchHandler>();
		services.AddSingleton<IPacketHandler, MatchChangeSlotHandler>();
		services.AddSingleton<IPacketHandler, MatchReadyHandler>();
		services.AddSingleton<IPacketHandler, MatchLockHandler>();
		services.AddSingleton<IPacketHandler, MatchChangeSettingsHandler>();
		services.AddSingleton<IPacketHandler, MatchStartHandler>();
		services.AddSingleton<IPacketHandler, MatchChangeModsHandler>();
		services.AddSingleton<IPacketHandler, MatchLoadCompleteHandler>();
		services.AddSingleton<IPacketHandler, MatchNoBeatmapHandler>();
		services.AddSingleton<IPacketHandler, MatchNotReadyHandler>();
		services.AddSingleton<IPacketHandler, MatchFailedHandler>();
		services.AddSingleton<IPacketHandler, MatchHasBeatmapHandler>();
		services.AddSingleton<IPacketHandler, MatchSkipRequestHandler>();
		services.AddSingleton<IPacketHandler, MatchTransferHostHandler>();
		services.AddSingleton<IPacketHandler, MatchChangeTeamHandler>();
		services.AddSingleton<IPacketHandler, MatchChangePasswordHandler>();
		services.AddSingleton<IPacketHandler, MatchScoreUpdateHandler>();
		services.AddSingleton<IPacketHandler, MatchCompleteHandler>();
		services.AddSingleton<IPacketHandler, MatchInviteHandler>();
		services.AddSingleton<IPacketHandler, TourneyMatchInfoRequestHandler>();
		services.AddSingleton<IPacketHandler, TourneyMatchJoinChannelHandler>();
		services.AddSingleton<IPacketHandler, TourneyMatchLeaveChannelHandler>();

		services.AddSingleton<IPacketHandler, StartSpectatingHandler>();
		services.AddSingleton<IPacketHandler, StopSpectatingHandler>();
		services.AddSingleton<IPacketHandler, SpectateFramesHandler>();
		services.AddSingleton<IPacketHandler, CantSpectateHandler>();

		services.AddSingleton<IPacketHandler, PingHandler>();
		services.AddSingleton<IPacketHandler, LogoutHandler>();
		services.AddSingleton<IPacketHandler, ChangeActionHandler>();
		services.AddSingleton<IPacketHandler, RequestStatusUpdateHandler>();
		services.AddSingleton<IPacketHandler, UserStatsRequestHandler>();
		services.AddSingleton<IPacketHandler, UserPresenceRequestHandler>();
		services.AddSingleton<IPacketHandler, UserPresenceRequestAllHandler>();
		services.AddSingleton<IPacketHandler, ReceiveUpdatesHandler>();
		services.AddSingleton<IPacketHandler, SetAwayMessageHandler>();
		services.AddSingleton<IPacketHandler, FriendAddHandler>();
		services.AddSingleton<IPacketHandler, FriendRemoveHandler>();

		services.AddSingleton<IPlayerLogoutHandler, LogoutBroadcastHandler>();

		services.AddSingleton<PacketDispatcher>();

		return services;
	}
}