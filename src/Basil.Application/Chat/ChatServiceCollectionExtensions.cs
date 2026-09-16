using Basil.Application.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Chat;

/// <summary>Registers the Chat slice's Application-layer services.</summary>
public static class ChatServiceCollectionExtensions
{
	/// <summary>Registers the Chat slice's Application-layer services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddChatApplication(this IServiceCollection services)
	{
		services.AddSingleton<ChannelMembershipService>();
		services.AddSingleton<ChatDispatchService>();
		services.AddSingleton<IPlayerLogoutHandler, ChannelPartLogoutHandler>();

		return services;
	}
}
