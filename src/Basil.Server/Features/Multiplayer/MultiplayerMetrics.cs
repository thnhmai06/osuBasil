using Basil.Server.Shared;
using System.Diagnostics.Metrics;

namespace Basil.Server.Features.Multiplayer;

/// <summary>Match-lock instruments published on <see cref="BasilMeter" />.</summary>
public static class MultiplayerMetrics
{
	/// <summary>Time spent waiting to acquire a <c>MatchSession.Lock</c>, in milliseconds.</summary>
	public static readonly Histogram<double> MatchLockWaitMs =
		BasilMeter.Instance.CreateHistogram<double>("basil.match.lock_wait.duration", "ms",
			"Time a caller spent waiting for a match's lock before acquiring it.");
}
