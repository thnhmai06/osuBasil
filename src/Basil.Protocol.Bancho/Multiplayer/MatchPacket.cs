using Basil.Protocol.Packets;

namespace Basil.Protocol.Multiplayer;

/// <summary>Wire-shape for a multiplayer match as it is written to the Bancho protocol.</summary>
/// <param name="Id">The id of the match.</param>
/// <param name="InProgress">
///     <see langword="true" /> if the match is currently in progress; otherwise,
///     <see langword="false" />.
/// </param>
/// <param name="Mods">The bitwise combination of mods applied to the match.</param>
/// <param name="Name">The name of the match.</param>
/// <param name="Password">The join password of the match, empty when no password is set.</param>
/// <param name="MapName">The name of the currently selected beatmap.</param>
/// <param name="MapId">The id of the currently selected beatmap.</param>
/// <param name="MapMd5">The md5 hash of the currently selected beatmap.</param>
/// <param name="Slots">The 16 match slots, one per player position.</param>
/// <param name="HostId">The id of the match host.</param>
/// <param name="Mode">The game mode of the match.</param>
/// <param name="WinCondition">The win condition of the match.</param>
/// <param name="TeamType">The team layout of the match.</param>
/// <param name="FreeMods">
///     <see langword="true" /> if each player selects their own mods; otherwise,
///     <see langword="false" />.
/// </param>
/// <param name="Seed">The random seed of the match.</param>
public sealed record MatchPacket(
	int Id,
	bool InProgress,
	int Mods,
	string Name,
	string Password,
	string MapName,
	int MapId,
	string MapMd5,
	IReadOnlyList<MatchSlotPacket> Slots,
	int HostId,
	int Mode,
	int WinCondition,
	int TeamType,
	bool FreeMods,
	int Seed);

/// <summary>One of a match's 16 slots, holding its current occupant and settings.</summary>
/// <param name="Status">The slot status bitmask.</param>
/// <param name="Team">The team the slot is assigned to.</param>
/// <param name="Mods">The mods selected for the slot, used when free mods are enabled.</param>
/// <param name="PlayerId">The id of the occupying player, or <see langword="null" /> when the slot is empty.</param>
public sealed record MatchSlotPacket(int Status, int Team, int Mods, int? PlayerId)
{
	/// <summary>Gets a value that indicates whether a player occupies the slot.</summary>
	/// <value><see langword="true" /> if the slot has an occupying player; otherwise, <see langword="false" />.</value>
	public bool HasPlayer => MatchSlotStatusMask.HasPlayer(Status);
}

/// <summary>
///     The slot-status bitmask that determines whether a slot has an occupying player. Shared so
///     <see cref="MatchSlotPacket.HasPlayer" /> and <see cref="PacketReader.ReadMatch" />
///     use the same check instead of each reimplementing the mask value.
/// </summary>
internal static class MatchSlotStatusMask
{
	private const int HasPlayerMask = 0b0111_1100;

	/// <summary>Determines whether a slot status value indicates an occupying player.</summary>
	/// <param name="status">The slot status value to test.</param>
	/// <returns><see langword="true" /> if the slot has an occupying player; otherwise, <see langword="false" />.</returns>
	public static bool HasPlayer(int status)
	{
		return (status & HasPlayerMask) != 0;
	}
}