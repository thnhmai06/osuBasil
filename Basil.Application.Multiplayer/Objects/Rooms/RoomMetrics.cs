using System.Diagnostics.Metrics;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public static class RoomMetrics
{
	public const string TimerFailedName = "basil.rooms.timer.failed";

	public static readonly Meter Meter = new("Basil.Multiplayer", "1.0");

	public static readonly Counter<long> TimerFailed = Meter
		.CreateCounter<long>(TimerFailedName, description: "Room timers whose work failed.");
}