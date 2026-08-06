using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the client's notification that the userSession has failed the current map.</summary>
/// <remarks>
///     Relays the failure to the match channel as a <c>MatchPlayerFailed</c> packet carrying the
///     userSession's slot id, so other clients can render the failure indicator for that slot. Slot state is
///     not changed. The relay runs under the match's
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchFailedHandler(MatchMembershipService matchMembership) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchFailed;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = gameSession.Match;
		if (match is null) return;

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			var slotId = match.GetSlotId(gameSession.Id);
			if (slotId is null) return;

			matchMembership.Enqueue(match, ServerPacketWriter.MatchPlayerFailed(slotId.Value), false);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}