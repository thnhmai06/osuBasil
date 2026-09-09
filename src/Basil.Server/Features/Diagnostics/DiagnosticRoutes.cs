using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Net.ServerSentEvents;
using Basil.Server.Features.Auth;
using Basil.Server.Shared.Eventing;
using Basil.Server.Shared.Http;
using Basil.Server.Shared.Http.OpenApi;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global

namespace Basil.Server.Features.Diagnostics;

/// <summary>
///     Registers the `/diagnostic` endpoints: a point-in-time reading and a live stream per category,
///     a curated live overview, and the one supported runtime action.
/// </summary>
/// <remarks>
///     Every route here requires administrator authorization; nothing under `/diagnostic` is
///     reachable without the admin key.
/// </remarks>
internal static class DiagnosticRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>Registers the `/diagnostic` routes on the `api.` host.</summary>
	/// <param name="group">The `api.{domain}` route group.</param>
	public static void MapDiagnosticRoutes(this RouteGroupBuilder group)
	{
		var diagnostics = group.MapGroup("/diagnostic").RequireAuthorization(AdminKeyDefaults.Policy);

		MapCategory(diagnostics, "process", DiagnosticStreams.Process,
			SampleWith<ProcessSampler, ProcessSample>(s => s.Sample()),
			SampleWith<ProcessSampler, ProcessSample>(s => s.Sample()),
			"Get a process and memory reading.",
			"""
			Reports the server process's own resource usage -- CPU, memory, thread and handle counts,
			uptime -- alongside the garbage collector's current memory-pressure view of the host or
			container it runs in.
			""",
			"Stream a process and memory reading live.",
			"""
			Server-Sent Events stream of the same reading `GET /diagnostic/process` returns, delivered
			about once per second. Every event is a complete reading, not a partial update.
			""",
			new ProcessSample(1234, TimeSpan.FromHours(2), 512 * 1024 * 1024, 480 * 1024 * 1024,
				600 * 1024 * 1024, 3.5, 42, 850, 64 * 1024 * 1024, 70 * 1024 * 1024, 1 * 1024 * 1024,
				8 * 1024 * 1024 * 1024L, 16L * 1024 * 1024 * 1024, 14L * 1024 * 1024 * 1024));

		MapCategory(diagnostics, "gc", DiagnosticStreams.Gc,
			SampleWith<GcSampler, GcSample>(s => s.Sample()), SampleWith<GcSampler, GcSample>(s => s.Sample()),
			"Get a garbage collector reading.",
			"""
			Reports the garbage collector's cumulative collection counts and its most recently
			completed collection's heap shape.
			""",
			"Stream a garbage collector reading live.",
			"""
			Server-Sent Events stream of the same reading `GET /diagnostic/gc` returns, delivered about
			once per second. Every event is a complete reading, not a partial update.
			""",
			new GcSample(120, 14, 2, 4 * 1024 * 1024, 8 * 1024 * 1024, 32 * 1024 * 1024, 512 * 1024,
				0, 256 * 1024, false, true, GCLatencyMode.Interactive, TimeSpan.FromMilliseconds(45),
				0.3, 900 * 1024 * 1024));

		MapCategory(diagnostics, "threadpool", DiagnosticStreams.ThreadPool,
			_ => ThreadPoolSnapshot.Capture(), _ => ThreadPoolSnapshot.Capture(),
			"Get a thread pool reading.",
			"Reports the thread pool's current sizing and workload counters.",
			"Stream a thread pool reading live.",
			"""
			Server-Sent Events stream of the same reading `GET /diagnostic/threadpool` returns,
			delivered about once per second. Every event is a complete reading, not a partial update.
			""",
			new ThreadPoolSample(12, 8, 32767, 20, 0, 4_500_000, 3));

		MapCategory(diagnostics, "runtime", DiagnosticStreams.Runtime,
			_ => RuntimeSnapshot.Capture(), _ => RuntimeSnapshot.Capture(),
			"Get the runtime and hardware configuration.",
			"""
			Reports the .NET runtime version, processor count, process/OS architecture and garbage
			collector mode the server is running under. Every value is fixed for the process's
			lifetime.
			""",
			"Stream the runtime and hardware configuration live.",
			"""
			Server-Sent Events stream of the same reading `GET /diagnostic/runtime` returns. Since
			every value is fixed for the process's lifetime, each delivered event repeats the same
			reading -- this exists so a dashboard can rely on one connection style across every
			category rather than treating `runtime` as a special case.
			""",
			new RuntimeSample(".NET 10.0.0", 8, Architecture.X64, Architecture.X64,
				"Microsoft Windows 10.0.26200", false, true));

		MapCategory(diagnostics, "exceptions", DiagnosticStreams.Exceptions,
			SampleWith<ExceptionsSampler, ExceptionsSample>(s => s.Sample()),
			SampleWith<ExceptionsSampler, ExceptionsSample>(s => s.Sample()),
			"Get the thrown-exception count.",
			"""
			Reports how many exceptions have been thrown anywhere in the process, whether or not they
			were caught and handled. Counting starts when the server starts, not necessarily at the
			moment shown by `GET /diagnostic/runtime`.
			""",
			"Stream the thrown-exception count live.",
			"""
			Server-Sent Events stream of the same reading `GET /diagnostic/exceptions` returns,
			delivered about once per second. Every event is a complete reading, not a partial update.
			""",
			new ExceptionsSample(7));

		MapCategory(diagnostics, "http", DiagnosticStreams.Http,
			SampleWith<HttpSampler, HttpSample>(s => s.Sample()),
			SampleWith<HttpSampler, HttpSample>(s => s.SampleAndResetDuration()),
			"Get an HTTP and connection reading.",
			"""
			Reports the web host's live request and connection counts, alongside the distribution of
			how long recently completed requests took.

			The request-duration distribution is a rolling window that empties every time the `live`
			stream below reports it; this plain reading only ever looks at whatever has built up in
			that window without emptying it, so checking it here never disturbs the live stream.
			""",
			"Stream an HTTP and connection reading live.",
			"""
			Server-Sent Events stream of the same shape `GET /diagnostic/http` returns, delivered about
			once per second. Unlike the plain reading, each event's request-duration distribution
			covers only the interval since the previous event -- the window is emptied every time this
			stream reports it.
			""",
			new HttpSample(3, 48291, 12, 6, 48200,
				new DurationAggregateSnapshot(120, 4.8, 0.002, 0.9,
					[0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10],
					[10, 40, 50, 15, 3, 1, 1, 0, 0, 0, 0, 0])));

		MapCategory(diagnostics, "application", DiagnosticStreams.Application,
			SampleWith<ApplicationSampler, ApplicationSample>(s => s.Sample()),
			SampleWith<ApplicationSampler, ApplicationSample>(s => s.Sample()),
			"Get a Basil application-state reading.",
			"""
			Reports Basil's own live state: logged-in osu! sessions, registered multiplayer matches and
			their running countdowns, registered chat channels, online IRC sessions, and the
			Server-Sent Events counters shared across every stream Basil publishes.
			""",
			"Stream a Basil application-state reading live.",
			"""
			Server-Sent Events stream of the same reading `GET /diagnostic/application` returns,
			delivered about once per second. Every event is a complete reading, not a partial update.
			""",
			new ApplicationSample(24, 9, 0, 3, 1, 5, 2));

		MapOverview(diagnostics);
		MapCollectAction(diagnostics);
	}

	/// <summary>
	///     Builds a <see cref="MapCategory{T}" /> sampling function that resolves <typeparamref name="TSampler" />
	///     from the request's own service provider and calls <paramref name="sample" /> on it.
	/// </summary>
	private static Func<IServiceProvider, T> SampleWith<TSampler, T>(Func<TSampler, T> sample)
		where TSampler : notnull =>
		services => sample(services.GetRequiredService<TSampler>());

	/// <summary>
	///     Registers a category's `GET /diagnostic/{category}` and `GET /diagnostic/{category}/live`
	///     pair, sharing everything mechanical between them so only the per-category text and sampling
	///     functions differ at each call site.
	/// </summary>
	/// <param name="samplePlain">Resolves a fresh reading from the request's own service provider. Used by the plain `GET`.</param>
	/// <param name="sampleLive">
	///     Resolves a fresh reading the same way. Used by the periodic live broadcast, and to seed a
	///     live connection that finds no prior stream state. Differs from <paramref name="samplePlain" />
	///     only for `http`.
	/// </param>
	private static void MapCategory<T>(RouteGroupBuilder group, string category, StreamKey key,
		Func<IServiceProvider, T> samplePlain, Func<IServiceProvider, T> sampleLive, string plainSummary,
		string plainDescription, string liveSummary, string liveDescription, T example) where T : notnull
	{
		group.MapGet($"/{category}", (HttpContext context) => Results.Json(samplePlain(context.RequestServices)))
			.WithGroupName("basilapi")
			.WithName($"getDiagnostic{Capitalize(category)}")
			.WithSummary(plainSummary)
			.WithDescription(plainDescription + $"\n\nSee also `GET /diagnostic/{category}/live`." + AdminKeyNote)
			.WithTags("Diagnostics")
			.Produces<T>()
			.WithExample(StatusCodes.Status200OK, example);

		group.MapGet($"/{category}/live",
				(HttpContext context, ILiveEventHub hub, CancellationToken cancellationToken) =>
				{
					SseEndpoints.SetSseHeaders(context);
					var services = context.RequestServices;
					return TypedResults.ServerSentEvents(
						StreamCategory(hub, key, category, () => sampleLive(services), cancellationToken));
				})
			.WithGroupName("basilapi")
			.WithName($"getDiagnostic{Capitalize(category)}Live")
			.WithSummary(liveSummary)
			.WithDescription(liveDescription + AdminKeyNote)
			.WithTags("Diagnostics")
			.Produces<T>()
			.WithExample(StatusCodes.Status200OK, example);
	}

	/// <summary>Registers the curated `GET /diagnostic/live` overview stream.</summary>
	private static void MapOverview(RouteGroupBuilder group)
	{
		group.MapGet("/live", (HttpContext context, ILiveEventHub hub, DiagnosticOverviewSampler sampler,
				CancellationToken cancellationToken) =>
			{
				SseEndpoints.SetSseHeaders(context);
				return TypedResults.ServerSentEvents(StreamCategory(hub, DiagnosticStreams.Overview, "overview",
					sampler.Sample, cancellationToken));
			})
			.WithGroupName("basilapi")
			.WithName("getDiagnosticOverviewLive")
			.WithSummary("Stream a curated overview of the most important live diagnostics.")
			.WithDescription("""
			                 Server-Sent Events stream of a small, curated subset of Basil's diagnostics --
			                 process CPU and memory, time spent in garbage collection, active HTTP requests,
			                 exceptions thrown, logged-in sessions, running matches, and connected live-stream
			                 subscribers -- delivered about once per second.

			                 This is not the union of every diagnostic category; it exists for watching one
			                 screen at a glance. See `GET /diagnostic/{category}/live` for any individual
			                 category's full detail.
			                 """ + AdminKeyNote)
			.WithTags("Diagnostics")
			.Produces<DiagnosticOverviewSample>()
			.WithExample(StatusCodes.Status200OK,
				new DiagnosticOverviewSample(3.5, 512 * 1024 * 1024, 0.3, 3, 7, 24, 9, 6));
	}

	/// <summary>Registers the one supported diagnostic action, `POST /diagnostic/gc/collect`.</summary>
	private static void MapCollectAction(RouteGroupBuilder group)
	{
		group.MapPost("/gc/collect", (GcSampler sampler) =>
			{
				GC.Collect();
				return Results.Json(sampler.Sample());
			})
			.WithGroupName("basilapi")
			.WithName("collectGarbage")
			.WithSummary("Force an immediate garbage collection.")
			.WithDescription("""
			                 Triggers a full, blocking garbage collection and returns the resulting reading, the
			                 same shape `GET /diagnostic/gc` returns.

			                 Intended for telling apart genuinely retained memory from memory that simply
			                 hasn't been collected yet; not needed for normal operation.
			                 """ + AdminKeyNote)
			.WithTags("Diagnostics")
			.Produces<GcSample>()
			.WithExample(StatusCodes.Status200OK,
				new GcSample(121, 14, 3, 512 * 1024, 1 * 1024 * 1024, 30 * 1024 * 1024, 0, 0, 0, false,
					true, GCLatencyMode.Interactive, TimeSpan.FromMilliseconds(52), 0.4, 900 * 1024 * 1024));
	}

	/// <summary>
	///     Turns one <see cref="LiveSubscription" /> into an SSE event stream: a first-ever subscriber
	///     to a stream nobody has published to yet builds and seeds its own reading immediately, rather
	///     than waiting for the next broadcast tick, so connecting is never slower than the category's
	///     own <c>GET</c>.
	/// </summary>
	private static async IAsyncEnumerable<SseItem<string>> StreamCategory<T>(ILiveEventHub hub, StreamKey key,
		string eventType, Func<T> sample, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var streamTag = new KeyValuePair<string, object?>("stream", eventType);
		using var subscription = hub.Open(key);
		EventingMetrics.SseActiveSubscribers.Add(1, streamTag);
		try
		{
			if (subscription.Snapshot is null)
			{
				var fence = subscription.Version;
				var seed = JsonSerializer.SerializeToUtf8Bytes(sample(), BasilJsonOptions.Instance);
				subscription.SeedIfNotSuperseded(seed, fence);
			}

			if (subscription.Snapshot is { } snapshot)
				yield return new SseItem<string>(Encoding.UTF8.GetString(snapshot.Span), eventType)
					{ ReconnectionInterval = SseEndpoints.ReconnectionInterval };

			await foreach (var item in subscription.Events.WithCancellation(cancellationToken))
				yield return new SseItem<string>(Encoding.UTF8.GetString(item.Payload.Span), eventType)
					{ ReconnectionInterval = SseEndpoints.ReconnectionInterval };
		}
		finally
		{
			EventingMetrics.SseActiveSubscribers.Add(-1, streamTag);
		}
	}

	private static string Capitalize(string value) => char.ToUpperInvariant(value[0]) + value[1..];
}
