namespace Basil.Protocol.Game.Stable.Models.Multiplayer;

public sealed record RoomStatePacket(
	int Id,
	bool InProgress,
	int Powerplay,
	int Mods,
	string Name,
	string Password,
	string MapName,
	int MapId,
	string MapMd5,
	IReadOnlyList<int> SlotStatuses,
	IReadOnlyList<int> SlotTeams,
	IReadOnlyList<int> SlotIds,
	int HostId,
	int Mode,
	int WinCondition,
	int TeamType,
	bool FreeMods,
	IReadOnlyList<int> SlotMods,
	int Seed);