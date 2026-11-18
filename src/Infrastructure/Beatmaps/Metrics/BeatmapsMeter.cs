using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Beatmaps.Metrics;

internal static class BeatmapsMeter
{
	internal static readonly Meter Instance = new("Basil.Beatmaps", "1.0");
}