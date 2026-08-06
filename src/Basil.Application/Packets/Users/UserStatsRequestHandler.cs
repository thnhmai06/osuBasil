using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Users;

/// <summary>
///     Handles the UserStatsRequest packet, which asks for the current stats of a specific set of
///     players.
/// </summary>
/// <remarks>
///     The body is an i16-counted list of 32-bit user ids. The handler answers with a user-stats
///     packet for each requested id that is online and not restricted, plus always for the requester
///     themselves. Requests for offline or restricted players are skipped.
/// </remarks>
public sealed class UserStatsRequestHandler(ISessionRegistry<GameSession> sessionRegistry) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.UserStatsRequest;

	public bool AllowedWhenRestricted => true;

	public Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var requestedIds = reader.ReadI32ListI16L();
		var unrestrictedIds = sessionRegistry.All.Where(s => !s.Restricted).Select(s => s.Id).ToHashSet();

		foreach (var id in requestedIds)
		{
			if (id == gameSession.Id || !unrestrictedIds.Contains(id)) continue;

			var target = sessionRegistry.GetByUserId(id);
			if (target is not null) gameSession.Enqueue(PacketBuilders.BuildUserStats(target));
		}

		return Task.CompletedTask;
	}
}