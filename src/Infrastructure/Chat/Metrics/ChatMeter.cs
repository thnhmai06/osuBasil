using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Chat.Metrics;

internal static class ChatMeter
{
	internal static readonly Meter Instance = new("Basil.Chat", "1.0");
}