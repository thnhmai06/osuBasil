using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Diagnostics;

/// <summary>The meter of the diagnostics module, on which the time every message takes is recorded.</summary>
internal static class BasilMeter
{
	/// <summary>The tag that carries the type of the message; the set of message types is finite.</summary>
	internal const string MessageTag = "message";

	/// <summary>Gets the meter.</summary>
	internal static readonly Meter Instance = new("Basil.Diagnostics", "1.0");

	/// <summary>Message duration in milliseconds, tagged with the type of the message.</summary>
	internal static readonly Histogram<double> MessageDuration =
		Instance.CreateHistogram<double>("basil.mediator.message.duration", "ms", "Time a message takes to be handled.");
}
