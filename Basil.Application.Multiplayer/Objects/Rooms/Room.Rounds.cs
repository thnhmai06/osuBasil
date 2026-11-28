using Basil.Application.Multiplayer.Objects.Events;
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

	// Lazy rule: a round that waits for players who never loaded is settled by the next operation that enters the room.
	private void DropUnloadedPlayers()
	{
		if (State.Rounds.DropUnloaded(Now) is not { } progress) return;

		var slots = progress.Dropped!;
		Emit(new RoomRoundLoadExpired(this, progress.Round, slots));
		if (progress.AllLoaded) Emit(new RoomRoundAllLoaded(this, progress.Round, slots[0]));
		if (!progress.Completed) return;

		SettleRound();
		ReportIfEmpty();
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