using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Multiplayer.Metrics;

internal static class MultiplayerMeter
{
	internal static readonly Meter Instance = new("Basil.Multiplayer", "1.0");
}