using Basil.Application.Multiplayer.Runtime;
using Basil.Application.Servers.Runtime;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace Basil.Host.Rest.Sse;

/// <summary>Server diagnostics: live measurements and maintenance actions.</summary>
internal static class DiagnosticsRoutes
{
	private static readonly string[] Categories =
		["process", "gc", "threadpool", "runtime", "exceptions", "http", "application", "database", "bancho", "irc", "events", "sessions"];

	private static readonly string[] Actions = ["gc/collect", "caches/clear", "exceptions/reset"];

	public static void MapDiagnostics(this RouteGroupBuilder v1)
	{
		var diagnostics = v1.MapGroup("/diagnostics").WithTags("Diagnostics").RequireToken();
		diagnostics.MapGet("/", (HttpContext c, Server server) => StreamAsync(c, server, null)).Sse()
			.WithSummary("Watch every diagnostics category.")
			.WithDescription("""
				The response is streamed using Server-Sent Events. The first event is a full sample; after that a bundle arrives every second with the newest sample. Data is up to about one second old.
				""");
		diagnostics.MapGet("/{category}", (HttpContext c, string category, Server server) => StreamAsync(c, server, category)).Sse()
			.WithSummary("Watch one diagnostics category.")
			.WithDescription("""
				The categories are process, gc, threadpool, runtime, exceptions, http, application, database, bancho, irc, events and sessions. Streamed like the full diagnostics.
				""");
		diagnostics.MapPost("/{category}/{**action}", ActionAsync)
			.WithSummary("Run a maintenance action.")
			.WithDescription("The actions are `gc/collect`, `caches/clear` and `exceptions/reset`.");
	}

	private static object Sample(RuntimeSampler runtime, UserRegistry registry, Lobby lobby, SseHub hub, string? category)
	{
		var all = new Dictionary<string, object>(runtime.Sample())
		{
			["application"] = new
			{
				usersOnline = registry.Online.Count, rooms = lobby.Rooms.Count,
				roundsInProgress = lobby.Rooms.Values.Count(room => room.Rounds.InProgress), openStreams = hub.OpenStreams
			},
			["sessions"] = Enum.GetValues<ConnectionType>().ToDictionary(type => type.ToString().ToLowerInvariant(),
				type => registry.Online.Values.Sum(user => user.Connections.Count(c => c.Type == type))),
			["bancho"] = new
			{
				osu = registry.Online.Values.Sum(u => u.Connections.Count(c => c.Type == ConnectionType.Osu)),
				tourney = registry.Online.Values.Sum(u => u.Connections.Count(c => c.Type == ConnectionType.Tourney))
			},
			["irc"] = new { connections = registry.Online.Values.Sum(u => u.Connections.Count(c => c.Type == ConnectionType.Irc)) },
			["database"] = new { available = false },
			["events"] = new { available = false }
		};
		return category is null ? all : all[category];
	}

	private static async Task<IResult> StreamAsync(HttpContext context, Server server, string? category)
	{
		var access = server.ScopeTo(context.Connection()).Diagnostics.Access();
		if (!access.IsOk) return access.Refusal();
		if (category is not null && !Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
			return Api.NotFound("No such diagnostics category.");

		var services = context.RequestServices;
		var runtime = services.GetRequiredService<RuntimeSampler>();
		var registry = services.GetRequiredService<UserRegistry>();
		var lobby = services.GetRequiredService<Lobby>();
		var hub = services.GetRequiredService<SseHub>();
		category = category?.ToLowerInvariant();
		return await context.StreamAsync($"diagnostics:{category ?? "all"}",
			_ => Task.FromResult<(object, string?)>((Sample(runtime, registry, lobby, hub, category), null)), null,
			_ => Task.FromResult(new SsePoll([new SseEvent(Sample(runtime, registry, lobby, hub, category))])));
	}

	private static async Task<IResult> ActionAsync(HttpContext context, string category, string action, Server server,
		RuntimeSampler runtime, ICacheCleaner? caches)
	{
		var access = server.ScopeTo(context.Connection()).Diagnostics.Access();
		if (!access.IsOk) return access.Refusal();

		switch ($"{category}/{action}".ToLowerInvariant())
		{
			case "gc/collect":
				RuntimeSampler.Collect();
				return Api.Done("Garbage collected");
			case "caches/clear":
				return Api.Ok(new { deleted = caches?.Clear() ?? 0 }, "Cache cleared");
			case "exceptions/reset":
				runtime.ResetExceptions();
				return Api.Done("Exceptions reset");
			default:
				return Api.NotFound("No such action.");
		}
	}
}

/// <summary>Creates the sampler when the server starts, so it counts from the beginning.</summary>
internal sealed class DiagnosticsStarter(RuntimeSampler sampler) : IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken)
	{
		_ = sampler.Sample();
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
