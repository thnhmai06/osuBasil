namespace Basil.Domain.Multiplayer.Room;

public enum RoomActionResult : byte
{
	Denied = 0,

	Ok = 1,

	RoleDenied,

	AlreadySeated,

	Banned,

	InAnotherRoom,

	WrongPassword,

	Full,

	IsObserver,

	NotInRoom,

	IsManager,

	NotBanned,

	TargetOffline,

	AlreadyInRoom,

	TooManyReferees,

	AlreadyReferee,

	NotReferee,

	IsCreator,

	IsPlayer,

	NotObserver,

	TooManyRooms,

	NoRoomId,

	SlotNotOpen,

	RoomLocked,

	Locked,

	InProgress,

	OwnSlot,

	NoTeams,

	NotFreemod,

	SpeedModNotAllowed,

	NoBeatmap,

	NotPlaying,

	NotInProgress,

	RoundMismatch,

	RoomClosed,

	Blocked,

	NotFound
}