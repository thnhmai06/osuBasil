using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets;

/// <summary>
///     Assembles user-presence and user-stats Bancho packets from a session's in-memory state.
/// </summary>
/// <remarks>
///     These helpers read only <see cref="UserSession" /> state kept in memory, such as
///     geolocation, privilege, and the current mode's stats, so they can be called freely by login
///     and packet-handling code that needs to send or re-send a userSession's presence and stats without
///     touching the database.
/// </remarks>
public static class PacketBuilders
{
	/// <summary>Builds a user-presence packet describing the given session.</summary>
	/// <param name="session">The session whose presence data is serialized.</param>
	/// <returns>A byte array containing the wrapped presence packet.</returns>
	public static byte[] BuildUserPresence(UserSession session)
	{
		return ServerPacketWriter.UserPresence(
			session.Id, session.Name, session.UtcOffset, (int)session.Geoloc.Country,
			(int)session.BanchoPrivilege, (int)session.Status.Mode,
			session.Geoloc.Longitude, session.Geoloc.Latitude, session.CurrentStats?.Rank ?? 0);
	}

	/// <summary>Builds a user-stats packet describing the given session's status and current-mode stats.</summary>
	/// <param name="session">The session whose status and stats are serialized.</param>
	/// <returns>A byte array containing the wrapped user-stats packet.</returns>
	public static byte[] BuildUserStats(UserSession session)
	{
		return ServerPacketWriter.UserStats(
			session.Id, (int)session.Status.UserActivity, session.Status.InfoText, session.Status.MapMd5,
			(int)session.Status.Mods, (int)session.Status.Mode, session.Status.MapId,
			session.CurrentStats?.RankedScore ?? 0, 100.0, session.CurrentStats?.Plays ?? 0,
			session.CurrentStats?.TotalScore ?? 0, session.CurrentStats?.Rank ?? 0, 0);
	}
}