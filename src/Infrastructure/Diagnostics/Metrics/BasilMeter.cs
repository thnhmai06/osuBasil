using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Diagnostics.Metrics;

internal static class BasilMeter
{
	internal const string MessageTag = "message";

	internal static readonly Meter Instance = new("Basil.Diagnostics", "1.0");

	internal static readonly Histogram<double> MessageDuration =
		Instance.CreateHistogram<double>("basil.mediator.message.duration", "ms",
			"Time a message takes to be handled.");
}