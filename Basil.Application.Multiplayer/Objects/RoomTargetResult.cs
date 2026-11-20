using System.Diagnostics;
using Basil.Domain.Multiplayer.Room;

namespace Basil.Application.Multiplayer.Objects;

/// <summary>Why an action on another user in a room (kick, ban, unban, referee, host, slot, team) was accepted or refused.</summary>
public enum RoomTargetResult : byte
{
	Denied = 0,

	Ok = 1,

	NotFound,

	RoomClosed,

	InProgress,

	NotInRoom,

	NotBanned,

	IsManager,

	IsCreator,

	AlreadyReferee,

	NotReferee,

	SlotNotOpen,

	RoomLocked,

	NoTeams,

	TargetOffline,

	Banned
}

internal static class RoomTargetResults
{
	/// <summary>Gives the target result that means the same as a refusal of the room's rules.</summary>
	internal static RoomTargetResult Of(RoomActionResult result)
	{
		return result switch
		{
			RoomActionResult.Ok => RoomTargetResult.Ok,
			RoomActionResult.RoomClosed => RoomTargetResult.RoomClosed,
			RoomActionResult.InProgress => RoomTargetResult.InProgress,
			RoomActionResult.NotInRoom => RoomTargetResult.NotInRoom,
			RoomActionResult.Banned => RoomTargetResult.Banned,
			RoomActionResult.NotBanned => RoomTargetResult.NotBanned,
			RoomActionResult.IsManager => RoomTargetResult.IsManager,
			RoomActionResult.IsCreator => RoomTargetResult.IsCreator,
			RoomActionResult.AlreadyReferee => RoomTargetResult.AlreadyReferee,
			RoomActionResult.NotReferee => RoomTargetResult.NotReferee,
			RoomActionResult.SlotNotOpen => RoomTargetResult.SlotNotOpen,
			RoomActionResult.RoomLocked => RoomTargetResult.RoomLocked,
			RoomActionResult.NoTeams => RoomTargetResult.NoTeams,
			_ => throw new UnreachableException($"An action on a target cannot return {result}.")
		};
	}
}
