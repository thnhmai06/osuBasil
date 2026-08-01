using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Handles the client's notification that the player has failed the current map.</summary>
/// <remarks>
///     Relays the failure to the match channel as a <c>MatchPlayerFailed</c> packet carrying the
///     player's slot id, so other clients can render the failure indicator for that slot. Slot state is
///     not changed. The relay runs under the match's
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchFailedHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchFailed;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: fail notifications are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the match-failed packet for the given player.</summary>
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
			var slotId = match.GetSlotId(player.Id);
			if (slotId is null) return;

			matchMembership.Enqueue(match, ServerPacketWriter.MatchPlayerFailed(slotId.Value), false);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}