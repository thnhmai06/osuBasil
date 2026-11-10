using Basil.Application.Common;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

/// <summary>Round operations on a room, scoped to a connection with its permissions.</summary>
public readonly struct ScopedRoomRounds
{
	private readonly ScopedRoom _scoped;

	/// <summary>Creates a scoped rounds service for a room.</summary>
	/// <param name="scoped">The scoped room.</param>
	internal ScopedRoomRounds(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Starts the next round; players who have the beatmap start playing.</summary>
	/// <returns>Ok, NotAuthorized, InProgress, NoBeatmap or RoomClosed when the room has closed.</returns>
	public Task<Result<RoomOutcome>> StartAsync()
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.RoundsStart) ??
		                       _scoped.Room.StartRound());
	}

	/// <summary>Checks that the connection may start rounds, without starting one.</summary>
	/// <returns><see langword="null" /> when allowed; otherwise the refusal that <see cref="StartAsync" /> answers.</returns>
	public Result<RoomOutcome>? RequireStart()
	{
		return _scoped.Require(RoomPermissions.RoundsStart);
	}

	/// <summary>Aborts the round in progress; players go back to not ready.</summary>
	/// <returns>Ok, NotAuthorized, NotInProgress or RoomClosed when the room has closed.</returns>
	public Task<Result<RoomOutcome>> AbortAsync()
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.RoundsAbort) ??
		                       _scoped.Room.AbortRound());
	}

	/// <summary>Reports that the caller finished loading the beatmap.</summary>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed; reporting again returns Ok and does nothing.</returns>
	/// <remarks>
	///     The last player to load emits <see cref="RoomRoundAllLoaded" /> instead of
	///     <see cref="RoomRoundPlayerLoaded" />.
	/// </remarks>
	public Task<Result<RoomOutcome>> MarkLoadedAsync()
	{
		return Task.FromResult<Result<RoomOutcome>>(_scoped.Room.MarkLoaded(_scoped.Actor));
	}

	/// <summary>Reports that the caller wants to skip the intro.</summary>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed; asking again returns Ok and does nothing.</returns>
	/// <remarks>
	///     The last player to ask emits <see cref="RoomRoundAllSkipped" /> instead of
	///     <see cref="RoomRoundPlayerSkipped" />.
	/// </remarks>
	public Task<Result<RoomOutcome>> SkipAsync()
	{
		return Task.FromResult<Result<RoomOutcome>>(_scoped.Room.SkipIntro(_scoped.Actor));
	}

	/// <summary>Reports that the caller failed; the player keeps playing until completion.</summary>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed.</returns>
	public Task<Result<RoomOutcome>> FailAsync()
	{
		return Task.FromResult<Result<RoomOutcome>>(_scoped.Room.ReportFailed(_scoped.Actor));
	}

	/// <summary>Reports that the caller completed the beatmap.</summary>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed.</returns>
	/// <remarks>
	///     When the last player completes, the round ends and <see cref="RoomRoundCompleted" /> is emitted instead of
	///     <see cref="RoomRoundPlayerCompleted" />.
	/// </remarks>
	public Task<Result<RoomOutcome>> CompleteAsync()
	{
		return Task.FromResult<Result<RoomOutcome>>(_scoped.Room.ReportCompleted(_scoped.Actor));
	}
}