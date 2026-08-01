using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Handles the host's request to lock or unlock a slot in the match.</summary>
/// <remarks>
///     Only the host may lock or unlock a slot. Locking toggles: a locked slot is opened again, and any
///     other slot is locked. The host is prevented from locking their own slot, which would effectively
///     kick them out of the room. The target slot id is bounds-checked against the fixed sixteen-slot
///     layout and the updated state is broadcast. The read-mutate-broadcast sequence runs under the
///     match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchLockHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchLock;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: slot locking is not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the match-lock packet for the given player.</summary>
	/// <param name="player">The player session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the payload holding the target slot id.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var slotId = reader.ReadI32();

		var match = player.Match;
		if (match is null || player.Id != match.HostId || slotId is < 0 or >= 16) return;

		await match.Lock.WaitAsync();
		try
		{
			var slot = match.Slots[slotId];

			if (slot.Status == SlotStatus.Locked)
			{
				slot.Status = SlotStatus.Open;
			}
			else
			{
				if (slot.PlayerId == player.Id)
					// don't allow the host to kick themselves by clicking their own crown.
					return;

				slot.Status = SlotStatus.Locked;
			}

			await matchMembership.EnqueueStateAsync(match);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}