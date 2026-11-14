using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Users.Metrics;

/// <summary>The meter of the Users module; its instruments carry no ids or names as labels.</summary>
internal static class UsersMeter
{
	/// <summary>Gets the meter.</summary>
	internal static readonly Meter Instance = new("Basil.Users", "1.0");
}