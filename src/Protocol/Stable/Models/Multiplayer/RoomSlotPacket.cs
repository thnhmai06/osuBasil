namespace Basil.Protocol.Stable.Models.Multiplayer;

public sealed record RoomSlotPacket(int Status, int Team, int Mods, int? PlayerId)
{
	private const int HasPlayerMask = 0b0111_1100;

	public bool HasPlayer => HasPlayerStatus(Status);

	internal static bool HasPlayerStatus(int status)
	{
		return (status & HasPlayerMask) != 0;
	}
}