using Basil.Server.Shared;
using System.Diagnostics.Metrics;

namespace Basil.Server.Features.Chat;

/// <summary>
///     Publishes the live channel count <see cref="IChannelRegistry" /> owns as an observable gauge
///     on <see cref="BasilMeter" />, for the process's whole lifetime. Diagnostics reads this back
///     through the meter instead of holding a reference to this slice's own registry.
/// </summary>
/// <remarks>
///     The callback runs on the meter's own collection thread and must never take a lock, await, or
///     walk a large structure. <see cref="IChannelRegistry.All" /> is backed by a
///     <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}" />, whose
///     <c>Values.Count</c> is an O(1) field read, not a walk.
/// </remarks>
public sealed class ChatMetricsPublisher(IChannelRegistry channels) : IHostedService
{

	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken)
	{
		BasilMeter.Instance.CreateObservableGauge("basil.channels.active",
			() => channels.All.Count,
			description: "The number of chat channels currently registered.");
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken)
	{
		// Nothing to release. An instrument's lifetime is its meter's, and the meter here is the
		// process-wide singleton, so the gauge stops being collected when the process does.
		return Task.CompletedTask;
	}
}
