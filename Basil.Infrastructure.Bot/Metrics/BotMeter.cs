using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Bot.Metrics;

internal static class BotMeter
{
	internal static readonly Meter Instance = new("Basil.Bot", "1.0");
}