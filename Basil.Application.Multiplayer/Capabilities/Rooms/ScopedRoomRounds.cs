using Basil.Domain.Multiplayer.Room;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

public readonly struct ScopedRoomRounds
{
	private readonly ScopedRoom _scoped;

	internal ScopedRoomRounds(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	public Task<RoomActionResult> StartAsync()
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.RoundsStart) ??
		                       _scoped.Room.StartRound());
	}

	public RoomActionResult? RequireStart()
	{
		return _scoped.Require(RoomPrivileges.RoundsStart);
	}

	public Task<RoomActionResult> AbortAsync()
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.RoundsAbort) ??
		                       _scoped.Room.AbortRound());
	}

	public Task<RoomActionResult> MarkLoadedAsync()
	{
		return Task.FromResult(_scoped.Room.MarkLoaded(_scoped.Actor));
	}

	public Task<RoomActionResult> SkipAsync()
	{
		return Task.FromResult(_scoped.Room.SkipIntro(_scoped.Actor));
	}

	public Task<RoomActionResult> FailAsync()
	{
		return Task.FromResult(_scoped.Room.ReportFailed(_scoped.Actor));
	}

	public Task<RoomActionResult> CompleteAsync()
	{
		return Task.FromResult(_scoped.Room.ReportCompleted(_scoped.Actor));
	}
}