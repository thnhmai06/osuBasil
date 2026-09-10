using Basil.Server.Shared;
using Basil.Server.Shared.Sessions;
using System.Diagnostics.Metrics;

namespace Basil.Server.Features.Irc;

/// <summary>
///     Publishes the live IRC session count <see cref="ISessionRegistry{TSession}" /> owns as an
///     observable gauge on <see cref="BasilMeter" />, for the process's whole lifetime. Diagnostics
///     reads this back through the meter instead of holding a reference to this slice's own registry.
/// </summary>
/// <remarks>
///     The callback runs on the meter's own collection thread and must never take a lock, await, or
///     walk a large structure. <see cref="ISessionRegistry{TSession}.All" /> is backed by a
///     <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}" />, whose
///     <c>Values.Count</c> is an O(1) field read, not a walk.
/// </remarks>
public sealed class IrcMetricsPublisher(ISessionRegistry<IrcSession> sessions) : IHostedService
{

	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken)
	{
		BasilMeter.Instance.CreateObservableGauge("basil.irc.sessions.active",
			() => sessions.All.Count,
			description: "The number of IRC sessions currently online.");
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
