using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Multiplayer;

/// <summary>The meter of the Multiplayer module; its instruments carry no ids or names as labels.</summary>
internal static class MultiplayerMeter
{
	/// <summary>Gets the meter.</summary>
	internal static readonly Meter Instance = new("Basil.Multiplayer", "1.0");
}
