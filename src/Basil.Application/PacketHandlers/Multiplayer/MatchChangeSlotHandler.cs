using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Handles the client's request to move to a different slot in the match.</summary>
/// <remarks>
///     Reads the target slot id and bounds-checks it against the fixed sixteen-slot layout. The move is
///     refused if the target slot is not currently open or if the player has no slot of their own. The
///     player's existing slot contents are copied into the target slot via
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSlot.CopyFrom" /> and the old slot is reset
///     to open, then the updated state is broadcast. The read-mutate-broadcast sequence runs under the
///     match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchChangeSlotHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchChangeSlot;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: slot changes are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the change-slot packet for the given player.</summary>
	/// <param name="player">The player session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the payload holding the target slot id.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var slotId = reader.ReadI32();

		var match = player.Match;
		if (match is null || slotId is < 0 or >= 16) return;

		await match.Lock.WaitAsync();
		try
		{
			if (match.Slots[slotId].Status != SlotStatus.Open) return;

			var slot = match.GetSlot(player.Id);
			if (slot is null) return;

			match.Slots[slotId].CopyFrom(slot);
			slot.Reset();

			await matchMembership.EnqueueStateAsync(match);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}