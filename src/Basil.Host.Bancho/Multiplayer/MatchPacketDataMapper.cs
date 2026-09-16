using Basil.Application.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Host.Bancho.Multiplayer;

/// <summary>
///     Maps the richer <see cref="MatchSession" /> model onto the flat wire shape
///     <see cref="ServerPacketWriter.WriteMatch" /> needs.
/// </summary>
/// <remarks>
///     The real password is always passed through unmasked. <c>WriteMatch</c>'s own
///     <c>sendPassword</c> flag decides whether it is actually written to the wire or replaced with a
///     placeholder.
/// </remarks>
public static class MatchPacketDataMapper
{
	/// <summary>Converts a match session into its wire packet representation.</summary>
	/// <param name="match">The match to convert.</param>
	/// <returns>The <see cref="MatchPacket" /> describing the match.</returns>
	public static MatchPacket ToPacket(this MatchSession match)
	{
		return new MatchPacket(
			match.Id,
			match.InProgress,
			(int)match.Mods,
			match.Name,
			match.Password,
			match.MapName,
			match.MapId ?? -1,
			// The wire format has no "no map" concept for md5 either; "" is what the real bancho
			// protocol sends in that case, matching this server's pre-nullable-MapMd5 behavior on the wire.
			match.MapMd5 ?? "",
			[.. match.Slots.Select(s => new MatchSlotPacket((int)s.Status, (int)s.Team, (int)s.Mods, s.PlayerId))],
			// The wire format has no "no host" concept; BasilBot's id is what the real bancho protocol
			// sends in that case, matching this server's pre-nullable-HostId behavior on the wire.
			match.HostId ?? SystemUserIds.BasilBot,
			(int)match.Mode,
			(int)match.WinCondition,
			(int)match.TeamType,
			match.Freemods,
			match.Seed);
	}
}