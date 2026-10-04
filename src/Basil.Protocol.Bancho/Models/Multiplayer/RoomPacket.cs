namespace Basil.Protocol.Bancho.Models.Multiplayer;

/// <summary>Wire-shape for a multiplayer room as it is written to the Bancho protocol.</summary>
/// <param name="Id">The id of the room.</param>
/// <param name="InProgress">
///     <see langword="true" /> if the room is currently in progress; otherwise,
///     <see langword="false" />.
/// </param>
/// <param name="Mods">The bitwise combination of mods applied to the room.</param>
/// <param name="Name">The name of the room.</param>
/// <param name="Password">The join password of the room, empty when no password is set.</param>
/// <param name="MapName">The name of the currently selected beatmap.</param>
/// <param name="MapId">The id of the currently selected beatmap.</param>
/// <param name="MapMd5">The md5 hash of the currently selected beatmap.</param>
/// <param name="Slots">The 16 room slots, one per player position.</param>
/// <param name="HostId">The id of the room host.</param>
/// <param name="Mode">The game mode of the room.</param>
/// <param name="WinCondition">The win condition of the room.</param>
/// <param name="TeamType">The team layout of the room.</param>
/// <param name="FreeMods">
///     <see langword="true" /> if each player selects their own mods; otherwise,
///     <see langword="false" />.
/// </param>
/// <param name="Seed">The random seed of the room.</param>
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
	int Seed);