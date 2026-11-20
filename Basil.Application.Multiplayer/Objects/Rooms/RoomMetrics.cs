using System.Diagnostics.Metrics;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public static class RoomMetrics
{
	public const string TimerFailedName = "basil.rooms.timer.failed";

	public static readonly Counter<long> TimerFailed = new Meter("Basil.Multiplayer", "1.0")
		.CreateCounter<long>(TimerFailedName, description: "Room timers whose work failed.");
}