using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Users;
using Basil.Domain.Multiplayer.Runtime;
using Basil.Host.Bancho.Shared.Http;
using Basil.Protocol.Packets;

namespace Basil.Host.Bancho.Multiplayer.Packets;

/// <summary>Handles the client's request to move to a different slot in the match.</summary>
/// <remarks>
///     Reads the target slot id and bounds-checks it against the fixed sixteen-slot layout. The move is
///     refused if the target slot is not currently open or if the userSession has no slot of their own. The
///     userSession's existing slot contents are copied into the target slot via
///     <see cref="RoomSlot.CopyFrom" /> and the old slot is reset
///     to open, then the updated state is broadcast. The read-mutate-broadcast sequence runs under the
///     match's <see cref="MatchSession.Lock" />.
/// </remarks>
public sealed class MatchChangeSlotHandler(IUserCache userCache) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchChangeSlot;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var slotId = reader.ReadI32();

		var match = gameSession.Match;
		if (match is null || slotId is < 0 or >= 16) return;

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		if (match.Slots[slotId].Status != RoomSlotStatus.Open) return;

		var slot = match.GetSlot(userCache.Resolve(gameSession));
		if (slot is null) return;

		match.Slots[slotId].CopyFrom(slot);
		slot.Reset();
		mutation.PublishState();
	}
}