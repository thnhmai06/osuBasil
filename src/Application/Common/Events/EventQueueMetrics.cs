using System.Diagnostics.Metrics;

namespace Basil.Application.Common.Events;

/// <summary>The measurements about the event queue that the server publishes on its meter.</summary>
public static class EventQueueMetrics
{
	/// <summary>Gets the name of the instrument that counts events the full queue refused.</summary>
	public const string RejectedName = "basil.events.rejected";

	/// <summary>Counts the events dropped because the queue was at its ceiling.</summary>
	public static readonly Counter<long> Rejected = new Meter("Basil.Events", "1.0")
		.CreateCounter<long>(RejectedName, description: "Events dropped because the event queue was full.");
}
