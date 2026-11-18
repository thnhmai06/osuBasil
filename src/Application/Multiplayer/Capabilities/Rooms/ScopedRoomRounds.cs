using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

public readonly struct ScopedRoomRounds
{
	private readonly ScopedRoom _scoped;

	internal ScopedRoomRounds(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	public Task<RoomOutcome> StartAsync()
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.RoundsStart) ??
		                       _scoped.Room.StartRound());
	}

	public RoomOutcome? RequireStart()
	{
		return _scoped.Require(RoomPrivileges.RoundsStart);
	}

	public Task<RoomOutcome> AbortAsync()
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.RoundsAbort) ??
		                       _scoped.Room.AbortRound());
	}

	public Task<RoomOutcome> MarkLoadedAsync()
	{
		return Task.FromResult(_scoped.Room.MarkLoaded(_scoped.Actor));
	}

	public Task<RoomOutcome> SkipAsync()
	{
		return Task.FromResult(_scoped.Room.SkipIntro(_scoped.Actor));
	}

	public Task<RoomOutcome> FailAsync()
	{
		return Task.FromResult(_scoped.Room.ReportFailed(_scoped.Actor));
	}

	public Task<RoomOutcome> CompleteAsync()
	{
		return Task.FromResult(_scoped.Room.ReportCompleted(_scoped.Actor));
	}
}