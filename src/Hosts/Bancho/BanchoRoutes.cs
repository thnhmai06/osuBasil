using Basil.Application.Users.Connections;
using Basil.Domain.Auth;
using Basil.Application.Users.Users;
using Basil.Host.Bancho.Login;
using Basil.Host.Bancho.Packets;
using Basil.Host.Bancho.Support;
using Basil.Host.Bancho.Web;
using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Host.Bancho;

/// <summary>Maps the osu! client's endpoints: bancho on <c>c.</c> and its variants, the web on <c>osu.</c>.</summary>
public static class BanchoRoutes
{
	private static readonly string[] BanchoHosts = ["c", "ce", "c4", "c5", "c6"];

	/// <summary>Adds the services of the osu! host.</summary>
	/// <param name="services">The service collection.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddBanchoHost(this IServiceCollection services)
	{
		services.AddSingleton<Outbox>();
		services.AddSingleton<RateLimiter>();
		services.AddSingleton<LoginAttemptGuard>();
		services.AddSingleton<BanchoState>();
		services.AddSingleton<RestartAnnouncer>();
		services.AddSingleton<BanchoLogin>();
		services.AddSingleton<ChannelResolver>();
		services.AddSingleton<PacketHandlers>();
		services.AddSingleton<PacketDispatcher>();
		services.AddSingleton<WebAuthentication>();
		services.AddSingleton<Delivery.RoomAudience>();
		return services;
	}

	/// <summary>Serves the osu! client's endpoints for the requests addressed to its sub-domains.</summary>
	/// <param name="app">The application pipeline.</param>
	/// <returns><paramref name="app" />, for chaining.</returns>
	public static IApplicationBuilder UseBancho(this IApplicationBuilder app)
	{
		app.MapWhen(context => IsHost(context, BanchoHosts), branch =>
		{
			branch.UseRouting();
			branch.UseEndpoints(endpoints => endpoints.MapBancho());
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

	private static void MapBancho(this IEndpointRouteBuilder app)
	{
		app.MapGet("/", () => "cho");
		app.MapPost("/", ExchangeAsync);
	}

	private static async Task<IResult> ExchangeAsync(HttpContext context, UserRegistry registry, BanchoLogin login,
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

		if (registry.Find(token) is not { IsOpen: true, Type: ConnectionType.Osu or ConnectionType.Tourney } connection)
			return Results.Bytes(Outbox.Build(writer => writer.RestartServer(0)), "text/html; charset=UTF-8");

		registry.MarkActive(connection);
		await dispatcher.DispatchAsync(connection, body, cancellationToken);
		return Results.Bytes(outbox.Drain(connection), "text/html; charset=UTF-8");
	}
}
