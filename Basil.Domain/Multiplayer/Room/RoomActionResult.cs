namespace Basil.Domain.Multiplayer.Room;

/// <summary>Why a rule of a room accepted or refused an action.</summary>
public enum RoomActionResult : byte
{
	Denied = 0,

	Ok = 1,

	RoomClosed,

	InProgress,

	NotInRoom,

	Banned,

	NotBanned,

	Full,

	IsManager,

	IsCreator,

	AlreadyReferee,

	NotReferee,

	SlotNotOpen,

	RoomLocked,

	OwnSlot,

	NoTeams,

	NotFreemod,

	SpeedModNotAllowed,

	NoBeatmap,

	NotPlaying,

	NotInProgress,

	RoundMismatch,

	AlreadyMember,

	IsPlayer
}
