using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Chat.Persistence;
using Basil.Application.Chat.Registry;
using Basil.Application.Common.Events;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Chat;

/// <summary>Registers the runtime objects owned by Chat.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the Chat runtime objects.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddChatApplication(this IServiceCollection services)
	{
		services.AddSingleton(new GeneralChannelRegistry());
		services.AddSingleton<ChannelContext>(sp => new ChannelContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<ChannelEvent>>(),
			sp.GetRequiredService<GeneralChannelRegistry>(),
			sp.GetRequiredService<IRelationshipRepository>(),
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
			sp.GetRequiredService<UserRegistry>()));
		return services;
	}
}