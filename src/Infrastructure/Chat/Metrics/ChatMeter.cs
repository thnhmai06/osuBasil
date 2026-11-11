using System.Diagnostics.Metrics;

namespace Basil.Infrastructure.Chat.Metrics;

/// <summary>The meter of the Chat module; its instruments carry no ids or names as labels.</summary>
internal static class ChatMeter
{
	/// <summary>Gets the meter.</summary>
	internal static readonly Meter Instance = new("Basil.Chat", "1.0");
}