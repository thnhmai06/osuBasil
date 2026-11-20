namespace Basil.Application.Multiplayer.Objects;

/// <summary>Why opening a room was accepted or refused.</summary>
public enum LobbyResult : byte
{
	Denied = 0,

	Ok = 1,

	Locked,

	TooManyRooms,

	AlreadyInRoom,

	NoRoomId
}
