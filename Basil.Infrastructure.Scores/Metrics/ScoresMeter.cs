using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Scores.Metrics;

internal static class ScoresMeter
{
	internal static readonly Meter Instance = new("Basil.Scores", "1.0");
}