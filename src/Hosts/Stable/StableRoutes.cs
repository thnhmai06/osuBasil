using Basil.Application.Users.Registry;
using Basil.Domain.Auth;
using Basil.Host.Stable.Delivery;
using Basil.Host.Stable.Login;
using Basil.Host.Stable.Packets;
using Basil.Host.Stable.Support;
using Basil.Host.Stable.Web;

namespace Basil.Host.Stable;

/// <summary>Maps the osu! client's endpoints: stable on <c>c.</c> and its variants, the web on <c>osu.</c>.</summary>
public static class StableRoutes
{
	private static readonly string[] StableHosts = ["c", "ce", "c4", "c5", "c6"];

	/// <summary>Adds the services of the osu! host.</summary>
	/// <param name="services">The service collection.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddStableHost(this IServiceCollection services)
	{
		services.AddSingleton<Outbox>();
		services.AddSingleton<RateLimiter>();
		services.AddSingleton<LoginAttemptGuard>();
		services.AddSingleton<StableState>();
		services.AddSingleton<RestartAnnouncer>();
		services.AddSingleton(StableDocumentation.Document);
		services.AddSingleton<StableShutdown>();
		services.AddSingleton<StableLogin>();
		services.AddSingleton<ChannelResolver>();
		services.AddSingleton<PacketHandlers>();
		services.AddSingleton<PacketDispatcher>();
		services.AddSingleton<WebAuthentication>();
		services.AddSingleton<RoomAudience>();
		return services;
	}

	/// <summary>Serves the osu! client's endpoints for the requests addressed to its sub-domains.</summary>
	/// <param name="app">The application pipeline.</param>
	/// <returns><paramref name="app" />, for chaining.</returns>
	public static IApplicationBuilder UseStable(this IApplicationBuilder app)
	{
		app.MapWhen(context => IsHost(context, StableHosts), branch =>
		{
			branch.UseRouting();
			branch.UseEndpoints(endpoints => endpoints.MapStable());
		});
		app.MapWhen(context => IsHost(context, ["osu"]), branch =>
		{
			branch.UseRouting();
			branch.UseEndpoints(endpoints => endpoints.MapOsuWeb());
		});
		return app;
	}

	private static bool IsHost(HttpContext context, string[] labels)
	{
		var host = context.Request.Host.Host;
		return labels.Any(label => host.StartsWith(label + ".", StringComparison.OrdinalIgnoreCase));
	}

	private static void MapStable(this IEndpointRouteBuilder app)
	{
		var cho = app.MapGroup("").WithGroupName(StableDocumentation.Group).WithTags("Game server");
		cho.MapGet("/", () => "cho").WithSummary("Say that the game server of the osu! client is there.");
		cho.MapPost("/", ExchangeAsync)
			.WithSummary("Log in, or exchange packets.")
			.WithDescription(StableDocumentation.ExchangeDescription());
	}

	private static async Task<IResult> ExchangeAsync(HttpContext context, UserRegistry registry, StableLogin login,
		PacketDispatcher dispatcher, Outbox outbox, CancellationToken cancellationToken)
	{
		using var buffer = new MemoryStream();
		await context.Request.Body.CopyToAsync(buffer, cancellationToken);
		var body = buffer.ToArray();
		context.Response.Headers.ContentType = "text/html; charset=UTF-8";

		var token = context.Request.Headers["osu-token"].ToString();
		if (string.IsNullOrEmpty(token))
		{
			var (newToken, reply) = await login.LoginAsync(context, body, cancellationToken);
			context.Response.Headers["cho-token"] = newToken;
			return Results.Bytes(reply, "text/html; charset=UTF-8");
		}

		if (registry.Find(token) is not
		    { IsOpen: true, Type: SessionType.Stable or SessionType.StableTourney } connection)
			return Results.Bytes(Outbox.Build(writer => writer.RestartServer(0)), "text/html; charset=UTF-8");

		registry.MarkActive(connection);
		await dispatcher.DispatchAsync(connection, body, cancellationToken);
		return Results.Bytes(outbox.Drain(connection), "text/html; charset=UTF-8");
	}
}