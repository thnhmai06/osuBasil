namespace Basil.Protocol.Game.Stable.Models.Multiplayer;

public sealed record RoomPacket(
	int Id,
	bool InProgress,
	int Mods,
	string Name,
	string Password,
	string MapName,
	int MapId,
	string MapMd5,
	IReadOnlyList<RoomSlotPacket> Slots,
	int HostId,
	int Mode,
	int WinCondition,
	int TeamType,
	bool FreeMods,
	int Seed)
{
	public const int NoHostId = 0;
}