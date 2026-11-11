using Basil.Application.Shared.Diagnostics;
using Basil.Application.Diagnostics.Messages;
using Basil.Application.Diagnostics.Services;
using Basil.Gateway;
using Basil.Gateway.RateLimiting;
using Basil.Host.Rest.Http;
using Mediator;
using Basil.Application.Diagnostics.Objects;

namespace Basil.Host.Rest.Sse;

/// <summary>Server diagnostics: live measurements and maintenance actions.</summary>
internal static class DiagnosticsRoutes
{
	private static readonly string[] Categories = [.. DiagnosticsSample.Categories];

	private static readonly TimeSpan CollectInterval = TimeSpan.FromSeconds(30);

	private static readonly string[] Actions = ["gc/collect", "caches/clear", "exceptions/reset"];

	/// <summary>Maps the Diagnostics routes.</summary>
	public static void MapDiagnostics(this RouteGroupBuilder v1)
	{
		var diagnostics = v1.MapGroup("/diagnostics").WithTags("Diagnostics").RequireToken();
		diagnostics.MapGet("/",
				(HttpContext c, ISender sender, IDiagnosticsIntake intake) => StreamAsync(c, sender, intake, null))
			.Sse()
			.WithSummary("Stream every diagnostics category.")
			.WithDescription("""
			                 The response is streamed using Server-Sent Events. The first event is a full sample; after that a bundle arrives every second with the newest sample. Data is up to about one second old.
			                 """);
		diagnostics.MapGet("/{category}",
				(HttpContext c, string category, ISender sender, IDiagnosticsIntake intake) =>
					StreamAsync(c, sender, intake, category)).Sse()
			.WithSummary("Stream one diagnostics category.")
			.WithDescription("""
			                 The categories are process, gc, threadpool, runtime, exceptions, http, application, database, stable, irc, events and sessions. Streamed like the full diagnostics.
			                 """);
		diagnostics.MapPost("/{category}/{**action}", ActionAsync)
			.WithSummary("Create a run of a maintenance action.")
			.WithDescription(
				"The actions are `gc/collect`, `caches/clear` and `exceptions/reset`. `gc/collect` can run once every 30 seconds; a request sooner is answered with 429.");
	}

	private static async Task<object> SampleAsync(IDiagnosticsIntake intake, string? category,
		CancellationToken ct)
	{
		var sample = await intake.TakeSampleAsync(ct);
		return category is null ? sample.All : sample[category];
	}

	private static async Task<IResult> StreamAsync(HttpContext context, ISender sender, IDiagnosticsIntake intake,
		string? category)
	{
		var access = await sender.Send(new CheckDiagnosticsAccess(context.Connection()), context.RequestAborted);
		if (access != DiagnosticsOutcome.Ok) return access.Refusal();
		if (category is not null && !Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
			return Api.NotFound("No such diagnostics category.");

		category = category?.ToLowerInvariant();
		return await context.StreamAsync($"diagnostics:{category ?? "all"}",
			async ct => (await SampleAsync(intake, category, ct), null), null,
			async ct => new SsePoll([new SseEvent(await SampleAsync(intake, category, ct))]));
	}

	private static async Task<IResult> ActionAsync(HttpContext context, string category, string action, ISender sender,
		RateLimiter limiter)
	{
		var access = await sender.Send(new CheckDiagnosticsAccess(context.Connection()), context.RequestAborted);
		if (access != DiagnosticsOutcome.Ok) return access.Refusal();

		var actor = context.Connection();
		switch ($"{category}/{action}".ToLowerInvariant())
		{
			case "gc/collect":
				if (!limiter.TryAcquire("diagnostics:gc-collect", 1, CollectInterval))
				{
					context.Response.Headers.RetryAfter = ((int)CollectInterval.TotalSeconds).ToString();
					return Api.Error(StatusCodes.Status429TooManyRequests, "rateLimited",
						"Garbage can be collected once every 30 seconds.");
				}

				var collected = await sender.Send(new CollectGarbage(actor), context.RequestAborted);
				return collected == DiagnosticsOutcome.Ok ? Api.Done("Garbage collected") : collected.Refusal();
			case "caches/clear":
				var (cleared, deleted) = await sender.Send(new ClearCaches(actor), context.RequestAborted);
				return cleared == DiagnosticsOutcome.Ok ? Api.Ok(new { deleted }, "Cache cleared") : cleared.Refusal();
			case "exceptions/reset":
				var reset = await sender.Send(new ResetExceptions(actor), context.RequestAborted);
				return reset == DiagnosticsOutcome.Ok ? Api.Done("Exceptions reset") : reset.Refusal();
			default:
				return Api.NotFound("No such action.");
		}
	}
}