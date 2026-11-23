using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Sessions.Metrics;

internal static class SessionsMeter
{
	internal static readonly Meter Instance = new("Basil.Sessions", "1.0");
}