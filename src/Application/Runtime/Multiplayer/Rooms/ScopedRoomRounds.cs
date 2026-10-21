using System.Diagnostics.CodeAnalysis;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Runtime.Multiplayer.Rooms;

/// <summary>Round operations on a room, scoped to a connection with its permissions.</summary>
public sealed class ScopedRoomRounds
{
	private readonly ScopedRoom _scoped;

	/// <summary>Creates a scoped rounds service for a room.</summary>
	/// <param name="scoped">The scoped room.</param>
	internal ScopedRoomRounds(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Starts the next round; players who have the beatmap start playing.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, InProgress, NoBeatmap or RoomClosed when the room has closed.</returns>
	public async Task<RoomResult> StartAsync(CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.RoundsStart) is { } denied) return denied;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var room = _scoped.Room;
		if (room.Rounds.InProgress) return RoomResult.InProgress;
		if (room.Settings.Beatmap is null) return RoomResult.NoBeatmap;

		room.CancelCountdown(true);
		var (round, players) = room.BeginRound();
		room.Context.Events.Publish(new RoomRoundStarted(room, round, players));
		return RoomResult.Ok;
	}

	/// <summary>Starts a countdown in the room, optionally starting a round when it elapses.</summary>
	/// <param name="length">How long the countdown runs.</param>
	/// <param name="startsRound">Whether the countdown starts a round when it elapses.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, InvalidSettings, InProgress or RoomClosed when the room has closed.</returns>
	public async Task<RoomResult> StartCountdownAsync(TimeSpan length, bool startsRound,
		CancellationToken cancellationToken = default)
	{
		var required = RoomPermissions.RoundsCountdown;
		if (startsRound) required |= RoomPermissions.RoundsStart;
		if (_scoped.Require(required) is { } denied) return denied;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var room = _scoped.Room;
		if (length <= TimeSpan.Zero) return RoomResult.InvalidSettings;
		if (startsRound && room.Rounds.InProgress) return RoomResult.InProgress;

		room.StartCountdown(_scoped.By.Record, length, startsRound);
		return RoomResult.Ok;
	}

	/// <summary>Cancels the room's current countdown.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NoCountdown or RoomClosed when the room has closed.</returns>
	public async Task<RoomResult> CancelCountdownAsync(CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.RoundsCountdown) is { } denied) return denied;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		return _scoped.Room.CancelCountdown(true) ? RoomResult.Ok : RoomResult.NoCountdown;
	}

	/// <summary>Aborts the round in progress; players go back to not ready.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotInProgress or RoomClosed when the room has closed.</returns>
	public async Task<RoomResult> AbortAsync(CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.RoundsAbort) is { } denied) return denied;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var room = _scoped.Room;
		if (!room.Rounds.InProgress) return RoomResult.NotInProgress;

		room.CancelCountdown(true);
		var round = room.EndCurrentRound(true)!;
		room.Context.Events.Publish(new RoomRoundAborted(room, round));
		return RoomResult.Ok;
	}

	/// <summary>Reports that the caller finished loading the beatmap.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed; reporting again returns Ok and does nothing.</returns>
	/// <remarks>The last player to load emits <see cref="RoomRoundAllLoaded" /> instead of <see cref="RoomRoundPlayerLoaded" />.</remarks>
	public async Task<RoomResult> MarkLoadedAsync(CancellationToken cancellationToken = default)
	{
		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var room = _scoped.Room;
		if (!TryGetPlayingSlot(out var round, out var slot)) return RoomResult.NotPlaying;
		if (slot.Loaded is true) return RoomResult.Ok;

		slot.Loaded = true;
		if (!room.Rounds.AllLoadedAnnounced &&
		    room.Slots.Where(s => s.Status is RoomSlotStatus.Playing).All(s => s.Loaded is true))
		{
			room.Rounds.AllLoadedAnnounced = true;
			room.Context.Events.Publish(new RoomRoundAllLoaded(room, round, slot.Index));
		}
		else
		{
			room.Context.Events.Publish(new RoomRoundPlayerLoaded(room, round, slot.Index));
		}

		return RoomResult.Ok;
	}

	/// <summary>Reports that the caller wants to skip the intro.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed; asking again returns Ok and does nothing.</returns>
	/// <remarks>The last player to ask emits <see cref="RoomRoundAllSkipped" /> instead of <see cref="RoomRoundPlayerSkipped" />.</remarks>
	public async Task<RoomResult> SkipAsync(CancellationToken cancellationToken = default)
	{
		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var room = _scoped.Room;
		if (!TryGetPlayingSlot(out var round, out var slot)) return RoomResult.NotPlaying;
		if (slot.IntroSkipped is true) return RoomResult.Ok;

		slot.IntroSkipped = true;
		if (!room.Rounds.AllSkippedAnnounced &&
		    room.Slots.Where(s => s.Status is RoomSlotStatus.Playing).All(s => s.IntroSkipped is true))
		{
			room.Rounds.AllSkippedAnnounced = true;
			room.Context.Events.Publish(new RoomRoundAllSkipped(room, round, slot.Index));
		}
		else
		{
			room.Context.Events.Publish(new RoomRoundPlayerSkipped(room, round, slot.Index));
		}

		return RoomResult.Ok;
	}

	/// <summary>Reports that the caller failed; the player keeps playing until completion.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed.</returns>
	public async Task<RoomResult> FailAsync(CancellationToken cancellationToken = default)
	{
		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var room = _scoped.Room;
		if (!TryGetPlayingSlot(out var round, out var slot)) return RoomResult.NotPlaying;

		room.Context.Events.Publish(new RoomRoundPlayerFailed(room, round, slot.Index));
		return RoomResult.Ok;
	}

	/// <summary>Reports that the caller completed the beatmap.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed.</returns>
	/// <remarks>
	///     When the last player completes, the round ends and <see cref="RoomRoundCompleted" /> is emitted instead of
	///     <see cref="RoomRoundPlayerCompleted" />.
	/// </remarks>
	public async Task<RoomResult> CompleteAsync(CancellationToken cancellationToken = default)
	{
		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var room = _scoped.Room;
		if (!TryGetPlayingSlot(out var round, out var slot)) return RoomResult.NotPlaying;

		slot.SetStatus(RoomSlotStatus.Complete);
		if (room.Slots.Any(s => s.Status is RoomSlotStatus.Playing))
			room.Context.Events.Publish(new RoomRoundPlayerCompleted(room, round, slot.Index));
		else
			EndRound(room, slot.Index);
		return RoomResult.Ok;
	}

	private bool TryGetPlayingSlot([MaybeNullWhen(false)] out Round round, [MaybeNullWhen(false)] out RoomSlot slot)
	{
		if (_scoped.By is BanchoConnection connection &&
		    _scoped.Room.Rounds.CurrentRound is { } current &&
		    _scoped.Room.Slots.Find(connection) is { Status: RoomSlotStatus.Playing } playing)
		{
			round = current;
			slot = playing;
			return true;
		}

		round = null;
		slot = null;
		return false;
	}

	private static void EndRound(Room room, int slot)
	{
		var round = room.EndCurrentRound(false)!;
		room.Context.Events.Publish(new RoomRoundCompleted(room, round, slot));
	}
}