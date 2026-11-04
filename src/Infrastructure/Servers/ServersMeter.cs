using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Servers;

/// <summary>The meter of the Servers module; its instruments carry no ids or names as labels.</summary>
internal static class ServersMeter
{
	/// <summary>Gets the meter.</summary>
	internal static readonly Meter Instance = new("Basil.Servers", "1.0");
}
