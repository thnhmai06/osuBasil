using Basil.Protocol.Packets;

namespace Basil.Protocol.Multiplayer;

/// <summary>
///     The decoded shape of a multiplayer match, as produced by <see cref="PacketReader.ReadMatch" />.
/// </summary>
/// <param name="Id">The id of the match.</param>
/// <param name="InProgress">
///     <see langword="true" /> if the match is currently in progress; otherwise,
///     <see langword="false" />.
/// </param>
/// <param name="Powerplay">The legacy powerplay flag of the match, always zero for matches this server writes.</param>
/// <param name="Mods">The bitwise combination of mods applied to the match.</param>
/// <param name="Name">The name of the match.</param>
/// <param name="Password">The join password of the match, empty when no password is set.</param>
/// <param name="MapName">The name of the currently selected beatmap.</param>
/// <param name="MapId">The id of the currently selected beatmap.</param>
/// <param name="MapMd5">The md5 hash of the currently selected beatmap.</param>
/// <param name="SlotStatuses">The status bitmask for each of the 16 slots, in slot order.</param>
/// <param name="SlotTeams">The team assignment for each of the 16 slots, in slot order.</param>
/// <param name="SlotIds">The ids of the players in occupied slots, in slot order; empty slots contribute no entry.</param>
/// <param name="HostId">The id of the match host.</param>
/// <param name="Mode">The game mode of the match.</param>
/// <param name="WinCondition">The win condition of the match.</param>
/// <param name="TeamType">The team layout of the match.</param>
/// <param name="FreeMods">
///     <see langword="true" /> if each player selects their own mods; otherwise,
///     <see langword="false" />.
/// </param>
/// <param name="SlotMods">The per-slot mods for each of the 16 slots, present only when <see cref="FreeMods" /> is set.</param>
/// <param name="Seed">The random seed of the match.</param>
public sealed record MatchState(
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