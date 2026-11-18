namespace Basil.Application.Multiplayer.Objects;

public enum RoomOutcome : byte
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

	InvalidMods,

	InvalidSettings,

	NoBeatmap,

	NotPlaying,

	NotInProgress,

	RoundMismatch,

	RoomClosed,

	Blocked,

	NotFound
}