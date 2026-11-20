namespace Basil.Domain.Multiplayer.Room;

/// <summary>Why a rule of a room accepted or refused an action.</summary>
public enum RoomActionResult : byte
{
	/// <summary>The caller lacks the privileges the action needs.</summary>
	Denied = 0,

	/// <summary>The action was accepted.</summary>
	Ok = 1,

	/// <summary>The room is closed, so no action is accepted.</summary>
	RoomClosed,

	/// <summary>A round is in progress, which the action does not allow; also given when a round is started while one is running.</summary>
	InProgress,

	/// <summary>The user does not take part in the room, or holds no slot where the action needs one.</summary>
	NotInRoom,

	/// <summary>The user is banned from the room.</summary>
	Banned,

	/// <summary>The user is not banned from the room.</summary>
	NotBanned,

	/// <summary>No slot is open for the player.</summary>
	Full,

	/// <summary>The user is the creator or a referee, whom the action cannot target.</summary>
	IsManager,

	/// <summary>The user is the creator of the room.</summary>
	IsCreator,

	/// <summary>The user is already a referee.</summary>
	AlreadyReferee,

	/// <summary>The user is not a referee.</summary>
	NotReferee,

	/// <summary>The slot does not exist, is locked or is occupied.</summary>
	SlotNotOpen,

	/// <summary>The room's slots are locked, so players cannot change them.</summary>
	RoomLocked,

	/// <summary>The action would lock the acting user's own slot.</summary>
	OwnSlot,

	/// <summary>The room is not in a team mode.</summary>
	NoTeams,

	/// <summary>The room does not allow players to choose their own mods.</summary>
	NotFreemod,

	/// <summary>The mods include a speed-changing mod, which only the room sets.</summary>
	SpeedModNotAllowed,

	/// <summary>The room has no beatmap.</summary>
	NoBeatmap,

	/// <summary>The user is not playing in the round in progress.</summary>
	NotPlaying,

	/// <summary>No round is in progress, or there is no round to settle.</summary>
	NotInProgress,

	/// <summary>The score is not awaited: it is for another beatmap, from a player the round does not wait for, or the settlement is already closed.</summary>
	RoundMismatch,

	/// <summary>The user already takes part in the room.</summary>
	AlreadyMember,

	/// <summary>The user holds a slot, which the action does not allow.</summary>
	IsPlayer
}
