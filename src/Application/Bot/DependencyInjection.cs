using Basil.Application.Bot.Registry;
using Basil.Application.Bot.Replies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Application.Bot;

/// <summary>Registers the BasilBot application's objects.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the objects BasilBot needs.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddBotApplication(this IServiceCollection services)
	{
		services.AddSingleton(Random.Shared);
		services.TryAddSingleton(TimeProvider.System);
		services.AddSingleton(_ => ReplyDefinitions.In(typeof(Reply).Assembly).AsEnumerable());
		services.AddSingleton<MpTargets>();
		services.AddSingleton<RoomCountdowns>();
		return services;
	}
}