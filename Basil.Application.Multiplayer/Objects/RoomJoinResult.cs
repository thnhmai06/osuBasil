using System.Diagnostics;
using Basil.Domain.Multiplayer.Room;

namespace Basil.Application.Multiplayer.Objects;

/// <summary>Why a connection entering or leaving a room as a player or an observer was accepted or refused.</summary>
public enum RoomJoinResult : byte
{
	Denied = 0,

	Ok = 1,

	RoomClosed,

	NotInRoom,

	Banned,

	Full,

	AlreadySeated,

	InAnotherRoom,

	IsObserver,

	IsPlayer,

	NotObserver,

	WrongPassword
}

internal static class RoomJoinResults
{
	/// <summary>Gives the join result that means the same as a refusal of the room's seating rules.</summary>
	internal static RoomJoinResult Of(RoomActionResult result)
	{
		return result switch
		{
			RoomActionResult.Ok => RoomJoinResult.Ok,
			RoomActionResult.RoomClosed => RoomJoinResult.RoomClosed,
			RoomActionResult.NotInRoom => RoomJoinResult.NotInRoom,
			RoomActionResult.Banned => RoomJoinResult.Banned,
			RoomActionResult.Full => RoomJoinResult.Full,
			_ => throw new UnreachableException($"Seating cannot return {result}.")
		};
	}
}