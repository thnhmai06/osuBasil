using Basil.Application.Bot.Registry;
using Basil.Application.Bot.Replies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Application.Bot;

public static class DependencyInjection
{
	public static IServiceCollection AddBotApplication(this IServiceCollection services)
	{
		services.AddSingleton(Random.Shared);
		services.TryAddSingleton(TimeProvider.System);
		services.AddSingleton(_ => ReplyDefinitions.In(typeof(Reply).Assembly).AsEnumerable());
		services.AddSingleton<MpTargets>();
		return services;
	}
}