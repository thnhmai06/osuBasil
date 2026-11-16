namespace Basil.Application.Multiplayer.Registry;

/// <summary>What an open room is doing, as the room search tells apart.</summary>
public enum RoomStatus : byte
{
	/// <summary>A round is in progress.</summary>
	Playing,

	/// <summary>No round is in progress and players cannot change slot or team.</summary>
	Locked,

	/// <summary>No round is in progress and the room is not locked.</summary>
	Open
}
