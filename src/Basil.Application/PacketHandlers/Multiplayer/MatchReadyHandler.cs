using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Handles the client's notification that the player is ready.</summary>
/// <remarks>
///     Marks the player's slot as <see cref="Basil.Domain.Multiplayer.SlotStatus.Ready" />. The state
///     update is broadcast to match members but not the lobby. The read-mutate-broadcast sequence runs
///     under the match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchReadyHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchReady;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: readiness changes are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the match-ready packet for the given player.</summary>
	/// <param name="player">The player session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the start of the payload; this handler does not read the payload.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = player.Match;
		if (match is null) return;

		await match.Lock.WaitAsync();
		try
		{
			var slot = match.GetSlot(player.Id);
			if (slot is null) return;

			slot.Status = SlotStatus.Ready;
			await matchMembership.EnqueueStateAsync(match, false, cancellationToken);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}