using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Users.Metrics;

internal static class UsersMeter
{
	internal static readonly Meter Instance = new("Basil.Users", "1.0");
}