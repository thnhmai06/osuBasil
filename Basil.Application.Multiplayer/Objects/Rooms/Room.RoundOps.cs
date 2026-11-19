using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer.Room;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal RoomActionResult StartRound()
	{
		using (State.Enter())
		{
			var (outcome, round, players, previous) = State.StartRound(Now);
			if (outcome is not RoomActionResult.Ok) return outcome;

			SyncTimer();
			Publish(previous);
			Emit(new RoomRoundStarted(this, round!, [.. players.Select(player => SessionOf(player)!)]));
			return RoomActionResult.Ok;
		}
	}

	internal RoomActionResult AbortRound()
	{
		using (State.Enter())
		{
			var (outcome, round) = State.AbortRound(Now);
			if (outcome is not RoomActionResult.Ok) return outcome;

			SyncTimer();
			Emit(new RoomRoundAborted(this, round!));
			return RoomActionResult.Ok;
		}
	}

	internal RoomActionResult MarkLoaded(Session actor)
	{
		using (State.Enter())
		{
			if (PlayerOf(actor) is not { } player) return IsClosed ? RoomActionResult.RoomClosed : RoomActionResult.NotPlaying;

			var (outcome, signal) = State.MarkLoaded(player);
			if (signal is { } s)
			{
				if (s.All)
					Emit(new RoomRoundAllLoaded(this, s.Round, s.Slot));
				else
					Emit(new RoomRoundPlayerLoaded(this, s.Round, s.Slot));
			}

			return outcome;
		}
	}

	internal RoomActionResult SkipIntro(Session actor)
	{
		using (State.Enter())
		{
			if (PlayerOf(actor) is not { } player) return IsClosed ? RoomActionResult.RoomClosed : RoomActionResult.NotPlaying;

			var (outcome, signal) = State.SkipIntro(player);
			if (signal is { } s)
			{
				if (s.All)
					Emit(new RoomRoundAllSkipped(this, s.Round, s.Slot));
				else
					Emit(new RoomRoundPlayerSkipped(this, s.Round, s.Slot));
			}

			return outcome;
		}
	}

	internal RoomActionResult ReportFailed(Session actor)
	{
		using (State.Enter())
		{
			if (PlayerOf(actor) is not { } player) return IsClosed ? RoomActionResult.RoomClosed : RoomActionResult.NotPlaying;

			var (outcome, signal) = State.ReportFailed(player);
			if (signal is { } s) Emit(new RoomRoundPlayerFailed(this, s.Round, s.Slot));
			return outcome;
		}
	}

	internal RoomActionResult ReportCompleted(Session actor)
	{
		using (State.Enter())
		{
			if (PlayerOf(actor) is not { } player) return IsClosed ? RoomActionResult.RoomClosed : RoomActionResult.NotPlaying;

			var (outcome, signal) = State.ReportCompleted(player, Now);
			if (signal is { } s)
			{
				if (s.All)
					Emit(new RoomRoundCompleted(this, s.Round, s.Slot));
				else
					Emit(new RoomRoundPlayerCompleted(this, s.Round, s.Slot));
			}

			return outcome;
		}
	}
}