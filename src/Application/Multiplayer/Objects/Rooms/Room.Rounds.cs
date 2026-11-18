using Basil.Domain.Multiplayer;

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

	internal Round? EndCurrentRound(bool aborted)
	{
		var round = State.EndCurrentRound(aborted, Now);
		SyncTimer();
		return round;
	}

	internal RoomRoundProgress? AdvanceRound()
	{
		return State.AdvanceRound(Now);
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
