using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the client's request to move to a different slot in the match.</summary>
/// <remarks>
///     Reads the target slot id and bounds-checks it against the fixed sixteen-slot layout. The move is
///     refused if the target slot is not currently open or if the userSession has no slot of their own. The
///     userSession's existing slot contents are copied into the target slot via
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSlot.CopyFrom" /> and the old slot is reset
///     to open, then the updated state is broadcast. The read-mutate-broadcast sequence runs under the
///     match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchChangeSlotHandler(MatchMembershipService matchMembership) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchChangeSlot;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var slotId = reader.ReadI32();

		var match = gameSession.Match;
		if (match is null || slotId is < 0 or >= 16) return;

		await match.Lock.WaitAsync(cancellationToken);
		long version;
		try
		{
			if (match.Slots[slotId].Status != SlotStatus.Open) return;

			var slot = match.GetSlot(gameSession.Id);
			if (slot is null) return;

			match.Slots[slotId].CopyFrom(slot);
			slot.Reset();

			version = match.NextStateVersion();
		}
		finally
		{
			match.Lock.Release();
		}

		await matchMembership.EnqueueStateAsync(match, version, cancellationToken: cancellationToken);
	}
}