using System.Text.Json;
using Basil.Server.Shared.Eventing;
using Basil.Server.Shared.Http;

namespace Basil.Server.Features.Diagnostics;

/// <summary>
///     Ticks every diagnostic category once a second, publishing a fresh sample to
///     <see cref="ILiveEventHub" /> for whichever categories currently have a live subscriber.
/// </summary>
/// <remarks>
///     A category with no subscriber is not sampled at all: <see cref="ILiveEventHub.HasSubscribers" />
///     is checked before doing any work, so an idle diagnostic API costs nothing beyond the timer tick
///     itself, and the cost of an additional subscriber to an already-watched category is zero -- the
///     sample is taken once per tick and broadcast to everyone watching, not once per subscriber.
///
///     <see cref="RuntimeMeterListener" /> keeps running for the process's whole lifetime regardless
///     of whether this loop finds any subscribers -- that guard applies to sampling and broadcasting,
///     never to the listener whose accumulated baseline would be destroyed by stopping it.
///
///     The <c>http</c> category is the one exception to "sample fresh, independent of everyone else":
///     its request-duration distribution resets on read, so this loop is the single caller allowed to
///     reset it (<see cref="HttpSampler.SampleAndResetDuration" />), keeping the live stream's
///     per-tick window and the plain <c>GET</c>'s peek from racing over the same accumulator.
/// </remarks>
public sealed class DiagnosticBroadcastService(
	ILiveEventHub hub,
	ProcessSampler process,
	GcSampler gc,
	HttpSampler http,
	ExceptionsSampler exceptions,
	ApplicationSampler application,
	DiagnosticOverviewSampler overview) : BackgroundService
{
	private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

	private List<Action>? _ticks;

	/// <summary>
	///     Runs a single broadcast pass over every diagnostic category. Exposed so tests can drive one
	///     pass deterministically instead of waiting on the real one-second timer.
	/// </summary>
	public void RunOnce()
	{
		_ticks ??= BuildTicks();
		foreach (var tick in _ticks) tick();
	}

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TickInterval);

		while (await timer.WaitForNextTickAsync(stoppingToken))
			RunOnce();
	}

	private List<Action> BuildTicks() =>
	[
		Tick(DiagnosticStreams.Process, process.Sample),
		Tick(DiagnosticStreams.Gc, gc.Sample),
		Tick(DiagnosticStreams.ThreadPool, ThreadPoolSnapshot.Capture),
		Tick(DiagnosticStreams.Runtime, RuntimeSnapshot.Capture),
		Tick(DiagnosticStreams.Exceptions, exceptions.Sample),
		Tick(DiagnosticStreams.Http, http.SampleAndResetDuration),
		Tick(DiagnosticStreams.Application, application.Sample),
		Tick(DiagnosticStreams.Overview, overview.Sample)
	];

	/// <summary>
	///     Builds one category's per-tick action: skip entirely with no subscriber, otherwise sample,
	///     serialize and publish under a version number private to this one stream. The counter is
	///     only ever touched from this loop's own single thread, so a plain increment is enough.
	/// </summary>
	private Action Tick<T>(StreamKey key, Func<T> sample)
	{
		var version = 0L;
		return () =>
		{
			if (!hub.HasSubscribers(key)) return;
			var payload = JsonSerializer.SerializeToUtf8Bytes(sample(), BasilJsonOptions.Instance);
			hub.Publish(key, ++version, payload);
		};
	}
}
