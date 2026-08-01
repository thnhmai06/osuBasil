using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Core;

/// <summary>
///     Handles the UserStatsRequest packet, which asks for the current stats of a specific set of
///     players.
/// </summary>
/// <remarks>
///     The body is an i16-counted list of 32-bit user ids. The handler answers with a user-stats
///     packet for each requested id that is online and not restricted, plus always for the requester
///     themselves. Requests for offline or restricted players are skipped.
/// </remarks>
public sealed class UserStatsRequestHandler(IPlayerSessionRegistry sessionRegistry) : IBanchoPacketHandler
{
	/// <summary>The <see cref="ClientPackets.UserStatsRequest" /> packet type.</summary>
	public ClientPackets PacketId => ClientPackets.UserStatsRequest;

	/// <summary>Restricted players may request stats, so this handler is always available.</summary>
	public bool AllowedWhenRestricted => true;

	/// <summary>Enqueues a user-stats packet for each requested id that resolves to an eligible session.</summary>
	/// <param name="player">The player session that requested the stats data.</param>
	/// <param name="reader">The packet reader positioned at the UserStatsRequest body.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A completed task.</returns>
	public Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var requestedIds = reader.ReadI32ListI16L();
		var unrestrictedIds = sessionRegistry.All.Where(s => !s.Restricted).Select(s => s.Id).ToHashSet();

		foreach (var id in requestedIds)
		{
			if (id == player.Id || !unrestrictedIds.Contains(id)) continue;

			var target = sessionRegistry.GetById(id);
			if (target is not null) player.Enqueue(PacketBuilders.BuildUserStats(target));
		}

		return Task.CompletedTask;
	}
}