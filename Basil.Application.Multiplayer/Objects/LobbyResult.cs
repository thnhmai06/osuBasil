using Basil.Domain.Multiplayer.Room;

namespace Basil.Application.Multiplayer.Objects;

/// <summary>Why an action on the lobby or on a room was accepted or refused, including the reasons that only the lobby knows.</summary>
/// <remarks>The members from <see cref="RoomClosed" /> to <see cref="RoundMismatch" /> carry the same meaning as the members of <see cref="RoomActionResult" /> with the same name.</remarks>
public enum LobbyResult : byte
{
	Denied = 0,

	Ok = 1,

	RoomClosed = (byte)RoomActionResult.RoomClosed,

	InProgress = (byte)RoomActionResult.InProgress,

	NotInRoom = (byte)RoomActionResult.NotInRoom,

	Banned = (byte)RoomActionResult.Banned,

	NotBanned = (byte)RoomActionResult.NotBanned,

	Full = (byte)RoomActionResult.Full,

	IsManager = (byte)RoomActionResult.IsManager,

	IsCreator = (byte)RoomActionResult.IsCreator,

	AlreadyReferee = (byte)RoomActionResult.AlreadyReferee,

	NotReferee = (byte)RoomActionResult.NotReferee,

	SlotNotOpen = (byte)RoomActionResult.SlotNotOpen,

	RoomLocked = (byte)RoomActionResult.RoomLocked,

	OwnSlot = (byte)RoomActionResult.OwnSlot,

	NoTeams = (byte)RoomActionResult.NoTeams,

	NotFreemod = (byte)RoomActionResult.NotFreemod,

	SpeedModNotAllowed = (byte)RoomActionResult.SpeedModNotAllowed,

	NoBeatmap = (byte)RoomActionResult.NoBeatmap,

	NotPlaying = (byte)RoomActionResult.NotPlaying,

	NotInProgress = (byte)RoomActionResult.NotInProgress,

	RoundMismatch = (byte)RoomActionResult.RoundMismatch,

	NotFound = 64,

	WrongPassword,

	AlreadySeated,

	InAnotherRoom,

	IsObserver,

	IsPlayer,

	NotObserver,

	AlreadyInRoom,

	TargetOffline,

	TooManyRooms,

	NoRoomId,

	Locked,

	Blocked
}

/// <summary>Converts between the results of a room and the results of the lobby.</summary>
public static class LobbyResults
{
	/// <summary>Gives the lobby result that means the same as a result of a room.</summary>
	public static LobbyResult ToLobby(this RoomActionResult result)
	{
		return (LobbyResult)(byte)result;
	}

	/// <summary>Gives the room result that means the same as a lobby result, or <see langword="null" /> when only the lobby knows the reason.</summary>
	public static RoomActionResult? ToRoom(this LobbyResult result)
	{
		return result < LobbyResult.NotFound ? (RoomActionResult)(byte)result : null;
	}
}
