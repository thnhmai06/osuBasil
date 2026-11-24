using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Chat.Persistence;
using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Sessions.Registry;
using Basil.Application.Shared.Events;
using Basil.Application.Users.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Chat;

public static class DependencyInjection
{
	public static IServiceCollection AddChatApplication(this IServiceCollection services)
	{
		services.AddSingleton(new GeneralChannelRegistry());
		services.AddSingleton<ChannelContext>(sp => new ChannelContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<ChannelEvent>>(),
			sp.GetRequiredService<GeneralChannelRegistry>(),
			sp.GetRequiredService<RelationshipRegistry>(),
			sp.GetRequiredService<IChannelRepository>()));
		services.AddSingleton<SpectatorChannels>(sp =>
			new SpectatorChannels(sp.GetRequiredService<ChannelContext>()));
		services.AddSingleton<RoomChannels>(sp => new RoomChannels(sp.GetRequiredService<ChannelContext>()));
		services.AddSingleton<PmChannels>(sp => new PmChannels(sp.GetRequiredService<ChannelContext>()));
		services.AddSingleton<ChannelDirectory>(sp => new ChannelDirectory(
			sp.GetRequiredService<GeneralChannelRegistry>(),
			sp.GetRequiredService<Lobby>(),
			sp.GetRequiredService<RoomChannels>(),
			sp.GetRequiredService<SpectatorChannels>(),
			sp.GetRequiredService<PmChannels>(),
			sp.GetRequiredService<SessionRegistry>()));
		return services;
	}
}