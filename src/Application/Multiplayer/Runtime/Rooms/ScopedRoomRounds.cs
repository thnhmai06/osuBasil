using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Application.Multiplayer.Runtime.Events;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Runtime.Rooms;

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

	/// <summary>The longest countdown a room accepts.</summary>
	public static TimeSpan MaxCountdown => Room.MaxCountdown;

	/// <summary>Starts the next round; players who have the beatmap start playing.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, InProgress, NoBeatmap or RoomClosed when the room has closed.</returns>
	public Task<Result<RoomOutcome>> StartAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.RoundsStart) ??
		                       _scoped.Room.StartRound());
	}

	/// <summary>Starts a countdown in the room, optionally starting a round when it elapses.</summary>
	/// <param name="length">How long the countdown runs.</param>
	/// <param name="startsRound">Whether the countdown starts a round when it elapses.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     Ok, NotAuthorized, InvalidSettings (shorter than one second or longer than <see cref="MaxCountdown" />),
	///     InProgress or RoomClosed when the room has closed.
	/// </returns>
	public Task<Result<RoomOutcome>> StartCountdownAsync(TimeSpan length, bool startsRound,
		CancellationToken cancellationToken = default)
	{
		var required = RoomPermissions.RoundsCountdown;
		if (startsRound) required |= RoomPermissions.RoundsStart;

		return Task.FromResult(_scoped.Require(required) ??
		                       _scoped.Room.BeginCountdown(_scoped.By.Record, length, startsRound));
	}

	/// <summary>Cancels the room's current countdown.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NoCountdown or RoomClosed when the room has closed.</returns>
	public Task<Result<RoomOutcome>> CancelCountdownAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.RoundsCountdown) ??
		                       _scoped.Room.StopCountdown());
	}

	/// <summary>Aborts the round in progress; players go back to not ready.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotInProgress or RoomClosed when the room has closed.</returns>
	public Task<Result<RoomOutcome>> AbortAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.RoundsAbort) ??
		                       _scoped.Room.AbortRound());
	}

	/// <summary>Reports that the caller finished loading the beatmap.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed; reporting again returns Ok and does nothing.</returns>
	/// <remarks>
	///     The last player to load emits <see cref="RoomRoundAllLoaded" /> instead of
	///     <see cref="RoomRoundPlayerLoaded" />.
	/// </remarks>
	public Task<Result<RoomOutcome>> MarkLoadedAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Room.MarkLoaded(_scoped.By));
	}

	/// <summary>Reports that the caller wants to skip the intro.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed; asking again returns Ok and does nothing.</returns>
	/// <remarks>
	///     The last player to ask emits <see cref="RoomRoundAllSkipped" /> instead of
	///     <see cref="RoomRoundPlayerSkipped" />.
	/// </remarks>
	public Task<Result<RoomOutcome>> SkipAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Room.SkipIntro(_scoped.By));
	}

	/// <summary>Reports that the caller failed; the player keeps playing until completion.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed.</returns>
	public Task<Result<RoomOutcome>> FailAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Room.ReportFailed(_scoped.By));
	}

	/// <summary>Reports that the caller completed the beatmap.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed.</returns>
	/// <remarks>
	///     When the last player completes, the round ends and <see cref="RoomRoundCompleted" /> is emitted instead of
	///     <see cref="RoomRoundPlayerCompleted" />.
	/// </remarks>
	public Task<Result<RoomOutcome>> CompleteAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Room.ReportCompleted(_scoped.By));
	}
}