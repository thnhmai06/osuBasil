using Basil.Application.Users.Registry;
using Basil.Domain.Auth;
using Basil.Host.OsuStable.Delivery;
using Basil.Host.OsuStable.Login;
using Basil.Host.OsuStable.Packets;
using Basil.Host.OsuStable.Support;
using Basil.Host.OsuStable.Web;

namespace Basil.Host.OsuStable;

/// <summary>Maps the osu! client's endpoints: osuStable on <c>c.</c> and its variants, the web on <c>osu.</c>.</summary>
public static class OsuStableRoutes
{
	private static readonly string[] OsuStableHosts = ["c", "ce", "c4", "c5", "c6"];

	/// <summary>Adds the services of the osu! host.</summary>
	/// <param name="services">The service collection.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddOsuStableHost(this IServiceCollection services)
	{
		services.AddSingleton<Outbox>();
		services.AddSingleton<RateLimiter>();
		services.AddSingleton<LoginAttemptGuard>();
		services.AddSingleton<OsuStableState>();
		services.AddSingleton<RestartAnnouncer>();
		services.AddSingleton(OsuStableDocumentation.Document);
		services.AddSingleton<OsuStableShutdown>();
		services.AddSingleton<OsuStableLogin>();
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
	public static IApplicationBuilder UseOsuStable(this IApplicationBuilder app)
	{
		app.MapWhen(context => IsHost(context, OsuStableHosts), branch =>
		{
			branch.UseRouting();
			branch.UseEndpoints(endpoints => endpoints.MapOsuStable());
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

	private static void MapOsuStable(this IEndpointRouteBuilder app)
	{
		var cho = app.MapGroup("").WithGroupName(OsuStableDocumentation.Group).WithTags("Game server");
		cho.MapGet("/", () => "cho").WithSummary("Say that the game server of the osu! client is there.");
		cho.MapPost("/", ExchangeAsync)
			.WithSummary("Log in, or exchange packets.")
			.WithDescription(OsuStableDocumentation.ExchangeDescription());
	}

	private static async Task<IResult> ExchangeAsync(HttpContext context, UserRegistry registry, OsuStableLogin login,
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

		if (registry.Find(token) is not { IsOpen: true, Type: SessionType.OsuStable or SessionType.OsuStableTourney } connection)
			return Results.Bytes(Outbox.Build(writer => writer.RestartServer(0)), "text/html; charset=UTF-8");

		registry.MarkActive(connection);
		await dispatcher.DispatchAsync(connection, body, cancellationToken);
		return Results.Bytes(outbox.Drain(connection), "text/html; charset=UTF-8");
	}
}