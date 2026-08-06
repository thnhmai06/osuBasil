using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the client's request to skip the map intro.</summary>
/// <remarks>
///     Marks the userSession's slot as skipped and broadcasts a <c>MatchPlayerSkipped</c> packet for the
///     userSession to the match channel. When every slot that is still
///     <see cref="Basil.Domain.Multiplayer.SlotStatus.Playing" /> has also skipped, a <c>MatchSkip</c>
///     packet is broadcast so the whole room skips in sync; that final packet excludes the lobby. The
///     read-mutate-broadcast sequence runs under the match's
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchSkipRequestHandler(MatchMembershipService matchMembership) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchSkipRequest;

	public bool AllowedWhenRestricted => false;

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

			slot.Skipped = true;
			matchMembership.Enqueue(match, ServerPacketWriter.MatchPlayerSkipped(gameSession.Id));

			var everyoneSkipped = match.Slots.All(s => s.Status != SlotStatus.Playing || s.Skipped);
			if (everyoneSkipped) matchMembership.Enqueue(match, ServerPacketWriter.MatchSkip(), false);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}