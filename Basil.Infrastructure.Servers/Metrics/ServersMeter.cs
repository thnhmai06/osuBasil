using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Servers.Metrics;

internal static class ServersMeter
{
	internal static readonly Meter Instance = new("Basil.Servers", "1.0");
}