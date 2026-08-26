using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the host's request to lock or unlock a slot in the match.</summary>
/// <remarks>
///     Only the host may lock or unlock a slot. Locking toggles: a locked slot is opened again, and any
///     other slot is locked. The host is prevented from locking their own slot, which would effectively
///     kick them out of the room. The target slot id is bound-checked against the fixed sixteen-slot
///     layout, and the updated state is broadcast. The read-mutate-broadcast sequence runs under the
///     match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchLockHandler(MatchMembershipService matchMembership) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchLock;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var slotId = reader.ReadI32();

		var match = gameSession.Match;
		if (match is null || gameSession.Id != match.HostId || slotId is < 0 or >= 16) return;

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			// Re-checked under the lock: host status can only change under this same lock, so a
			// sender who lost host while waiting for it must not still act with host authority.
			if (gameSession.Id != match.HostId) return;

			var slot = match.Slots[slotId];

			if (slot.Status == SlotStatus.Locked)
			{
				slot.Status = SlotStatus.Open;
			}
			else
			{
				if (slot.PlayerId == gameSession.Id)
					// don't allow the host to kick themselves by clicking their own crown.
					return;

				slot.Status = SlotStatus.Locked;
			}

			await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}