using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Bot;

/// <summary>The meter of the Bot module; its instruments carry no ids or names as labels.</summary>
internal static class BotMeter
{
	/// <summary>Gets the meter.</summary>
	internal static readonly Meter Instance = new("Basil.Bot", "1.0");
}
