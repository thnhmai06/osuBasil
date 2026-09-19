namespace Basil.Protocol.Bancho.Models.Multiplayer;

/// <summary>One of a room's 16 slots, holding its current occupant and settings.</summary>
/// <param name="Status">The slot status bitmask.</param>
/// <param name="Team">The team the slot is assigned to.</param>
/// <param name="Mods">The mods selected for the slot, used when free mods are enabled.</param>
/// <param name="PlayerId">The id of the occupying player, or <see langword="null" /> when the slot is empty.</param>
public sealed record RoomSlotPacket(int Status, int Team, int Mods, int? PlayerId)
{
	private const int HasPlayerMask = 0b0111_1100;

	/// <summary>Gets a value that indicates whether a player occupies the slot.</summary>
	/// <value><see langword="true" /> if the slot has an occupying player; otherwise, <see langword="false" />.</value>
	public bool HasPlayer => IsHasPlayerStatus(Status);

	/// <summary>
	///     Determines whether a slot status bitmask marks the slot as occupied by a player.
	/// </summary>
	/// <param name="status">The slot status bitmask.</param>
	/// <returns>
	///     <see langword="true" /> if any of the occupied-slot bits
	///     (<c>0b0111_1100</c>) are set; otherwise, <see langword="false" />.
	/// </returns>
	/// <remarks>
	///     The mask covers the status bits that indicate an occupant; only the placeholder statuses
	///     -- empty, not ready, and ready -- which use the bits below the mask, report
	///     <see langword="false" />.
	/// </remarks>
	internal static bool IsHasPlayerStatus(int status)
	{
		return (status & HasPlayerMask) != 0;
	}
}