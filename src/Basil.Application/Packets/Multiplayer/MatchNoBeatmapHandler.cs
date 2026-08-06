using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the client's notification that the userSession does not have the match's beatmap.</summary>
/// <remarks>
///     Marks the userSession's slot as <see cref="Basil.Domain.Multiplayer.SlotStatus.NoMap" />, which
///     signals other clients that the userSession cannot play the selected map. The state update is
///     broadcast to match members but not the lobby. The read-mutate-broadcast sequence runs under the
///     match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchNoBeatmapHandler(MatchMembershipService matchMembership) : IPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchNoBeatmap;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: beatmap notifications are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the no-beatmap packet for the given userSession.</summary>
	/// <param name="gameSession">The userSession session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the start of the payload; this handler does not read the payload.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = gameSession.Match;
		if (match is null) return;

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			var slot = match.GetSlot(gameSession.Id);
			if (slot is null) return;

			slot.Status = SlotStatus.NoMap;
			await matchMembership.EnqueueStateAsync(match, false, cancellationToken);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}