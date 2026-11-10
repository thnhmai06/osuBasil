using System.Diagnostics.Metrics;

namespace Basil.Application.Multiplayer.Objects.Rooms;

/// <summary>The measurements about rooms that Multiplayer publishes on the server's meter.</summary>
public static class RoomMetrics
{
	/// <summary>Gets the name of the instrument that counts failed room timers.</summary>
	public const string TimerFailedName = "basil.rooms.timer.failed";

	/// <summary>Counts the room timers whose work failed.</summary>
	public static readonly Counter<long> TimerFailed = new Meter("Basil.Multiplayer", "1.0")
		.CreateCounter<long>(TimerFailedName, description: "Room timers whose work failed.");
}