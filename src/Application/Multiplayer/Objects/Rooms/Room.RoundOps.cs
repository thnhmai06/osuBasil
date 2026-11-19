using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer.Room;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal RoomOutcome StartRound()
	{
		using (State.Enter())
		{
			var (outcome, round, players, previous) = State.StartRound(Now);
			if (outcome is not RoomOutcome.Ok) return outcome;

			SyncTimer();
			Publish(previous);
			Emit(new RoomRoundStarted(this, round!, [.. players.Select(player => SessionOf(player)!)]));
			return RoomOutcome.Ok;
		}
	}

	internal RoomOutcome AbortRound()
	{
		using (State.Enter())
		{
			var (outcome, round) = State.AbortRound(Now);
			if (outcome is not RoomOutcome.Ok) return outcome;

			SyncTimer();
			Emit(new RoomRoundAborted(this, round!));
			return RoomOutcome.Ok;
		}
	}

	internal RoomOutcome MarkLoaded(Session actor)
	{
		using (State.Enter())
		{
			if (PlayerOf(actor) is not { } player) return IsClosed ? RoomOutcome.RoomClosed : RoomOutcome.NotPlaying;

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

	internal RoomOutcome SkipIntro(Session actor)
	{
		using (State.Enter())
		{
			if (PlayerOf(actor) is not { } player) return IsClosed ? RoomOutcome.RoomClosed : RoomOutcome.NotPlaying;

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

	internal RoomOutcome ReportFailed(Session actor)
	{
		using (State.Enter())
		{
			if (PlayerOf(actor) is not { } player) return IsClosed ? RoomOutcome.RoomClosed : RoomOutcome.NotPlaying;

			var (outcome, signal) = State.ReportFailed(player);
			if (signal is { } s) Emit(new RoomRoundPlayerFailed(this, s.Round, s.Slot));
			return outcome;
		}
	}

	internal RoomOutcome ReportCompleted(Session actor)
	{
		using (State.Enter())
		{
			if (PlayerOf(actor) is not { } player) return IsClosed ? RoomOutcome.RoomClosed : RoomOutcome.NotPlaying;

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