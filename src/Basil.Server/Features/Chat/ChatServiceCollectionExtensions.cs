using Basil.Server.Features.Chat.Packets;
using Basil.Server.Shared.Configuration;
using Basil.Server.Shared.Http.Bancho;
using Basil.Server.Shared.Persistence;
using Basil.Server.Shared.Sessions;
using Microsoft.Extensions.Options;

namespace Basil.Server.Features.Chat;

/// <summary>Registers the Chat slice's services.</summary>
public static class ChatServiceCollectionExtensions
{
	/// <summary>Registers the Chat slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddChat(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton<ChannelMembershipService>();
		services.AddSingleton<ChatDispatchService>();
		services.AddSingleton<IPlayerLogoutHandler, ChannelPartLogoutHandler>();

		services.AddSingleton<IChannelRegistry, InMemoryChannelRegistry>();

		services.AddSingleton<IChannelRepository>(sp => new SqliteChannelRepository(BuildConnectionString(sp)));

		services.AddSingleton<IPacketHandler, ChannelJoinHandler>();
		services.AddSingleton<IPacketHandler, ChannelPartHandler>();
		services.AddSingleton<IPacketHandler, LobbyJoinHandler>();
		services.AddSingleton<IPacketHandler, LobbyPartHandler>();
		services.AddSingleton<IPacketHandler, SendPublicMessageHandler>();
		services.AddSingleton<IPacketHandler, SendPrivateMessageHandler>();
		services.AddSingleton<IPacketHandler, ToggleBlockNonFriendDmsHandler>();

		services.AddHostedService<ChatMetricsPublisher>();

		return services;
	}

	private static string BuildConnectionString(IServiceProvider sp)
	{
		return sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Build();
	}
}