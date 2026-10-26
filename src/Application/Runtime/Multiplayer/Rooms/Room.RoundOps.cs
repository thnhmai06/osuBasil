using System.Diagnostics.CodeAnalysis;
using Basil.Application.Runtime.Common;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Multiplayer.Rooms;

/// <content>Round transitions: starting, aborting, countdowns and the players' reports of what they do.</content>
public sealed partial class Room
{
	/// <summary>The longest countdown a room accepts.</summary>
	public static readonly TimeSpan MaxCountdown = TimeSpan.FromHours(1);

	/// <summary>Starts the next round; players who have the beatmap start playing.</summary>
	/// <returns>Ok, InProgress, NoBeatmap or RoomClosed.</returns>
	internal Result<RoomOutcome> StartRound()
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Rounds.InProgress) return RoomOutcome.InProgress;
			if (Settings.Beatmap is null) return RoomOutcome.NoBeatmap;

			CancelCountdown(RoomCountdownCancelReason.RoundStarted);
			var (round, players) = BeginRound();
			Emit(new RoomRoundStarted(this, round, players));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Starts a countdown, optionally starting a round when it elapses.</summary>
	/// <param name="by">The user who starts the countdown.</param>
	/// <param name="length">How long the countdown runs.</param>
	/// <param name="startsRound">Whether the countdown starts a round when it elapses.</param>
	/// <returns>
	///     Ok, InvalidSettings (shorter than one second or longer than <see cref="MaxCountdown" />), InProgress or
	///     RoomClosed.
	/// </returns>
	internal Result<RoomOutcome> BeginCountdown(UserRecord by, TimeSpan length, bool startsRound)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (length < TimeSpan.FromSeconds(1) || length > MaxCountdown) return RoomOutcome.InvalidSettings;
			if (startsRound && Rounds.InProgress) return RoomOutcome.InProgress;

			StartCountdown(by, length, startsRound);
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Cancels the room's current countdown.</summary>
	/// <returns>Ok, NoCountdown or RoomClosed.</returns>
	internal Result<RoomOutcome> StopCountdown()
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;

			return CancelCountdown(RoomCountdownCancelReason.Aborted) ? RoomOutcome.Ok : RoomOutcome.NoCountdown;
		}
	}

	/// <summary>Aborts the round in progress; players go back to not ready.</summary>
	/// <returns>Ok, NotInProgress or RoomClosed.</returns>
	internal Result<RoomOutcome> AbortRound()
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!Rounds.InProgress) return RoomOutcome.NotInProgress;

			CancelCountdown(RoomCountdownCancelReason.RoundAborted);
			var round = EndCurrentRound(true)!;
			Emit(new RoomRoundAborted(this, round));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Records that a player finished loading the beatmap.</summary>
	/// <param name="by">The reporting connection.</param>
	/// <returns>Ok, NotPlaying or RoomClosed; reporting again returns Ok and does nothing.</returns>
	/// <remarks>
	///     The last player to load emits <see cref="RoomRoundAllLoaded" /> instead of
	///     <see cref="RoomRoundPlayerLoaded" />.
	/// </remarks>
	internal Result<RoomOutcome> MarkLoaded(Connection by)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!TryGetPlayingSlot(by, out var round, out var slot)) return RoomOutcome.NotPlaying;
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

	/// <summary>Records that a player wants to skip the intro.</summary>
	/// <param name="by">The reporting connection.</param>
	/// <returns>Ok, NotPlaying or RoomClosed; asking again returns Ok and does nothing.</returns>
	/// <remarks>
	///     The last player to ask emits <see cref="RoomRoundAllSkipped" /> instead of
	///     <see cref="RoomRoundPlayerSkipped" />.
	/// </remarks>
	internal Result<RoomOutcome> SkipIntro(Connection by)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!TryGetPlayingSlot(by, out var round, out var slot)) return RoomOutcome.NotPlaying;
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

	/// <summary>Records that a player failed; the player keeps playing until completion.</summary>
	/// <param name="by">The reporting connection.</param>
	/// <returns>Ok, NotPlaying or RoomClosed.</returns>
	internal Result<RoomOutcome> ReportFailed(Connection by)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!TryGetPlayingSlot(by, out var round, out var slot)) return RoomOutcome.NotPlaying;

			Emit(new RoomRoundPlayerFailed(this, round, slot.Index));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Records that a player completed the beatmap.</summary>
	/// <param name="by">The reporting connection.</param>
	/// <returns>Ok, NotPlaying or RoomClosed.</returns>
	/// <remarks>
	///     When the last player completes, the round ends and <see cref="RoomRoundCompleted" /> is emitted instead of
	///     <see cref="RoomRoundPlayerCompleted" />.
	/// </remarks>
	internal Result<RoomOutcome> ReportCompleted(Connection by)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!TryGetPlayingSlot(by, out var round, out var slot)) return RoomOutcome.NotPlaying;

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

	private bool TryGetPlayingSlot(Connection by, [MaybeNullWhen(false)] out Round round,
		[MaybeNullWhen(false)] out RoomSlot slot)
	{
		if (by is BanchoConnection connection &&
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