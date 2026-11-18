using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Content.Metrics;

internal static class ContentMeter
{
	internal static readonly Meter Instance = new("Basil.Content", "1.0");
}