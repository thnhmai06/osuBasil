using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Models.Multiplayer;

/// <summary>
///     The decoded shape of a multiplayer room, as produced by <see cref="PacketReader.ReadMatch" />.
/// </summary>
/// <param name="Id">The id of the room.</param>
/// <param name="InProgress">
///     <see langword="true" /> if the room is currently in progress; otherwise,
///     <see langword="false" />.
/// </param>
/// <param name="Powerplay">The legacy powerplay flag of the room, always zero for roomes this server writes.</param>
/// <param name="Mods">The bitwise combination of mods applied to the room.</param>
/// <param name="Name">The name of the room.</param>
/// <param name="Password">The join password of the room, empty when no password is set.</param>
/// <param name="MapName">The name of the currently selected beatmap.</param>
/// <param name="MapId">The id of the currently selected beatmap.</param>
/// <param name="MapMd5">The md5 hash of the currently selected beatmap.</param>
/// <param name="SlotStatuses">The status bitmask for each of the 16 slots, in slot order.</param>
/// <param name="SlotTeams">The team assignment for each of the 16 slots, in slot order.</param>
/// <param name="SlotIds">The ids of the players in occupied slots, in slot order; empty slots contribute no entry.</param>
/// <param name="HostId">The id of the room host.</param>
/// <param name="Mode">The game mode of the room.</param>
/// <param name="WinCondition">The win condition of the room.</param>
/// <param name="TeamType">The team layout of the room.</param>
/// <param name="FreeMods">
///     <see langword="true" /> if each player selects their own mods; otherwise,
///     <see langword="false" />.
/// </param>
/// <param name="SlotMods">The per-slot mods for each of the 16 slots, present only when <see cref="FreeMods" /> is set.</param>
/// <param name="Seed">The random seed of the room.</param>
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