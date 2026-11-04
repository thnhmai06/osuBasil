using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Scores;

/// <summary>The meter of the Scores module; its instruments carry no ids or names as labels.</summary>
internal static class ScoresMeter
{
	/// <summary>Gets the meter.</summary>
	internal static readonly Meter Instance = new("Basil.Scores", "1.0");
}
