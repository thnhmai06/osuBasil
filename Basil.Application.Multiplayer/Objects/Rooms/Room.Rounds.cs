using Basil.Domain.Multiplayer.Report.Rounds;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal static void Fire(Action action)
	{
		try
		{
			action();
		}
		catch (Exception)
		{
			// A timer has no caller to report to; the failure is counted.
			RoomMetrics.TimerFailed.Add(1);
		}
	}

	internal Round? AbortCurrentRound()
	{
		var round = State.Rounds.AbortRound(Now).Round;
		SyncTimer();
		return round;
	}

	private void SyncTimer()
	{
		if (_timer is null) return;
		if (Rounds.Settlement is { Closed: false } settlement && ReferenceEquals(settlement, _timerSettlement)) return;

		_timer.Dispose();
		_timer = null;
		_timerSettlement = null;
	}
}