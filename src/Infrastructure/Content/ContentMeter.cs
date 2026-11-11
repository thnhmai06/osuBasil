using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Content;

/// <summary>The meter of the Content module; its instruments carry no ids or names as labels.</summary>
internal static class ContentMeter
{
	/// <summary>Gets the meter.</summary>
	internal static readonly Meter Instance = new("Basil.Content", "1.0");
}