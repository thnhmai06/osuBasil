namespace Basil.Application.Multiplayer.Objects;

/// <summary>Why inviting a user to a room was accepted or refused.</summary>
public enum RoomInviteResult : byte
{
	Denied = 0,

	Ok = 1,

	RoomClosed,

	TargetOffline,

	AlreadyInRoom,

	Blocked
}