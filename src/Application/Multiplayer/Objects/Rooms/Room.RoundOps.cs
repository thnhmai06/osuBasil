using System.Diagnostics.CodeAnalysis;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal RoomOutcome StartRound()
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Rounds.InProgress) return RoomOutcome.InProgress;
			if (Settings.Beatmap is null) return RoomOutcome.NoBeatmap;

			var (round, players) = BeginRound();
			Emit(new RoomRoundStarted(this, round, players));
			return RoomOutcome.Ok;
		}
	}

	internal RoomOutcome AbortRound()
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!Rounds.InProgress) return RoomOutcome.NotInProgress;

			var round = EndCurrentRound(true)!;
			Emit(new RoomRoundAborted(this, round));
			return RoomOutcome.Ok;
		}
	}

	internal RoomOutcome MarkLoaded(Connection actor)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!TryGetPlayingSlot(actor, out var round, out var slot)) return RoomOutcome.NotPlaying;
			if (slot.Loaded is true) return RoomOutcome.Ok;

			slot.Loaded = true;
			if (!Rounds.AllLoadedAnnounced &&
			    Slots.Where(s => s.Status is RoomSlotStatus.Playing).All(s => s.Loaded is true))
			{
				Rounds.AllLoadedAnnounced = true;
				Emit(new RoomRoundAllLoaded(this, round, slot.Index));
			}
			else
			{
				Emit(new RoomRoundPlayerLoaded(this, round, slot.Index));
			}

			return RoomOutcome.Ok;
		}
	}

	internal RoomOutcome SkipIntro(Connection actor)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!TryGetPlayingSlot(actor, out var round, out var slot)) return RoomOutcome.NotPlaying;
			if (slot.IntroSkipped is true) return RoomOutcome.Ok;

			slot.IntroSkipped = true;
			if (!Rounds.AllSkippedAnnounced &&
			    Slots.Where(s => s.Status is RoomSlotStatus.Playing).All(s => s.IntroSkipped is true))
			{
				Rounds.AllSkippedAnnounced = true;
				Emit(new RoomRoundAllSkipped(this, round, slot.Index));
			}
			else
			{
				Emit(new RoomRoundPlayerSkipped(this, round, slot.Index));
			}

			return RoomOutcome.Ok;
		}
	}

	internal RoomOutcome ReportFailed(Connection actor)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!TryGetPlayingSlot(actor, out var round, out var slot)) return RoomOutcome.NotPlaying;

			Emit(new RoomRoundPlayerFailed(this, round, slot.Index));
			return RoomOutcome.Ok;
		}
	}

	internal RoomOutcome ReportCompleted(Connection actor)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!TryGetPlayingSlot(actor, out var round, out var slot)) return RoomOutcome.NotPlaying;

			slot.SetStatus(RoomSlotStatus.Complete);
			Rounds.Settlement?.Complete(slot.Player!.Record);
			if (Slots.Any(s => s.Status is RoomSlotStatus.Playing))
			{
				Emit(new RoomRoundPlayerCompleted(this, round, slot.Index));
			}
			else
			{
				var ended = EndCurrentRound(false)!;
				Emit(new RoomRoundCompleted(this, ended, slot.Index));
			}

			return RoomOutcome.Ok;
		}
	}

	private bool TryGetPlayingSlot(Connection actor, [MaybeNullWhen(false)] out Round round,
		[MaybeNullWhen(false)] out RoomSlot slot)
	{
		if (actor is StableConnection connection &&
		    Rounds.CurrentRound is { } current &&
		    Slots.Find(connection) is { Status: RoomSlotStatus.Playing } playing)
		{
			round = current;
			slot = playing;
			return true;
		}

		round = null;
		slot = null;
		return false;
	}
}