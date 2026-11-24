using Mediator;
using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using System.Diagnostics;
using Basil.Application.Diagnostics.Services;
using Basil.Application.Shared.Events;
using Basil.Domain.Sessions;
using Basil.Gateway.Admission;
using Basil.Gateway.RateLimiting;
using Basil.Host.Stable.Delivery;
using Basil.Host.Stable.Login;
using Basil.Host.Stable.Packets;
using Basil.Host.Stable.Support;
using Basil.Host.Stable.Web;

namespace Basil.Host.Stable.Routes;

public static class StableRoutes
{
	private static readonly string[] StableHosts = ["c", "ce", "c4", "c5", "c6"];

	private static readonly TimeSpan PollDeliveryWait = TimeSpan.FromMilliseconds(200);

	public static IServiceCollection AddStableHost(this IServiceCollection services)
	{
		services.AddSingleton<Outbox>();
		services.AddSingleton<RateLimiter>();
		services.AddSingleton<LoginAttemptGuard>();
		services.AddSingleton<StableState>();
		services.AddSingleton<RestartAnnouncer>();
		services.AddSingleton<StableShutdown>();
		services.AddSingleton<StableLogin>();
		services.AddSingleton<PacketHandlers>();
		services.AddSingleton<PacketDispatcher>();
		services.AddSingleton<WebAuthentication>();
		services.AddSingleton<RoomAudience>();
		services.AddSingleton<PresenceAudience>();
		return services;
	}

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

	internal static void MapStable(this IEndpointRouteBuilder app)
	{
		var cho = app.MapGroup("")
			.WithMetadata(new AdmissionPolicyAttribute()).AddEndpointFilter<StableAdmission>();
		cho.MapGet("/", () => "cho");
		cho.MapPost("/", ExchangeAsync);
	}

	private static async Task<IResult> ExchangeAsync(HttpContext context, SessionRegistry registry, ISender sender, StableLogin login,
		PacketDispatcher dispatcher, Outbox outbox, IPublishedEvents events, CancellationToken ct)
	{
		using var buffer = new MemoryStream();
		await context.Request.Body.CopyToAsync(buffer, ct);
		var body = buffer.ToArray();
		context.Response.Headers.ContentType = "text/html; charset=UTF-8";

		var token = context.Request.Headers["osu-token"].ToString();
		if (string.IsNullOrEmpty(token))
		{
			var (newToken, reply) = await login.LoginAsync(context, body, ct);
			context.Response.Headers["cho-token"] = newToken;
			return Results.Bytes(reply, "text/html; charset=UTF-8");
		}

		// An unknown token, as after a restart, makes the client sign in again.
		if (registry.Find(token) is not
		    { IsOpen: true, Type: SessionType.Stable or SessionType.StableTourney } connection)
			return Results.Bytes(Outbox.Build(writer => writer.RestartServer(0)), "text/html; charset=UTF-8");

		var started = Stopwatch.GetTimestamp();
		await sender.Send(new MarkSessionActive(connection), ct);
		// A session silent past its expiry is closed by the heartbeat: the client logs in again, as for a dead token.
		if (!connection.IsOpen)
			return Results.Bytes(Outbox.Build(writer => writer.RestartServer(0)), "text/html; charset=UTF-8");
		// The deliveries the packets cause are awaited briefly, so the client sees its own action in this answer.
		using (var published = events.Track())
		{
			await dispatcher.DispatchAsync(connection, body, ct);
			await published.DeliveredAsync(PollDeliveryWait, ct);
		}

		var packets = outbox.Drain(connection);
		DiagnosticsMetrics.StableDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
		return Results.Bytes(packets, "text/html; charset=UTF-8");
	}
}