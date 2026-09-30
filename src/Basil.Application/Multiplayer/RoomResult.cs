namespace Basil.Application.Multiplayer;

/// <summary>The outcome of an operation on the lobby or a room.</summary>
public enum RoomResult : byte
{
	/// <summary>The operation succeeded.</summary>
	Ok,

	/// <summary>The caller may not perform the operation.</summary>
	NotAuthorized,

	/// <summary>The user already has an open connection seated in the room.</summary>
	AlreadySeated,

	/// <summary>The user is banned from the room.</summary>
	Banned,

	/// <summary>The user is silenced.</summary>
	Silenced,

	/// <summary>The connection is seated in another room.</summary>
	InAnotherRoom,

	/// <summary>The password does not match the room's password.</summary>
	WrongPassword,

	/// <summary>The room has no open slots.</summary>
	Full,

	/// <summary>The user is observing the room.</summary>
	IsObserver,

	/// <summary>The connection is not seated in the room.</summary>
	NotInRoom,

	/// <summary>The target is the creator or a referee.</summary>
	IsManager,

	/// <summary>The user is not banned from the room.</summary>
	NotBanned,

	/// <summary>The invited user is offline or is the server's bot.</summary>
	TargetOffline,

	/// <summary>The user is already seated in the room.</summary>
	AlreadyInRoom,

	/// <summary>The room already has the maximum number of referees.</summary>
	TooManyReferees,

	/// <summary>The user is already a referee.</summary>
	AlreadyReferee,

	/// <summary>The user is not a referee.</summary>
	NotReferee,

	/// <summary>The target is the room's creator.</summary>
	IsCreator,

	/// <summary>The user is playing in the room.</summary>
	IsPlayer,

	/// <summary>The user is not observing the room.</summary>
	NotObserver,

	/// <summary>The creator already has the maximum number of tournament rooms open.</summary>
	TooManyRooms,

	/// <summary>Every room id is in use.</summary>
	NoRoomId,

	/// <summary>The target slot does not exist, is locked or is occupied.</summary>
	SlotNotOpen,

	/// <summary>The room is locked, so players cannot change slot or team.</summary>
	RoomLocked,

	/// <summary>A round is in progress.</summary>
	InProgress,

	/// <summary>A player cannot lock their own slot.</summary>
	OwnSlot,

	/// <summary>The room's team type does not use teams.</summary>
	NoTeams,

	/// <summary>Freemod is off, so players cannot choose their own mods.</summary>
	NotFreemod,

	/// <summary>Speed-changing mods are chosen for the whole room, not per player.</summary>
	SpeedModNotAllowed,

	/// <summary>The mods are not valid for the room's game mode.</summary>
	InvalidMods,

	/// <summary>A setting has a value that is not allowed.</summary>
	InvalidSettings
}