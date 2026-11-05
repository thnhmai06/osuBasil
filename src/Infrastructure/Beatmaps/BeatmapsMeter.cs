using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Beatmaps;

/// <summary>The meter of the Beatmaps module; its instruments carry no ids or names as labels.</summary>
internal static class BeatmapsMeter
{
	/// <summary>Gets the meter.</summary>
	internal static readonly Meter Instance = new("Basil.Beatmaps", "1.0");
}