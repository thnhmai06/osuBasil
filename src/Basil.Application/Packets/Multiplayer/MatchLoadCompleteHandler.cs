using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the client's notification that the userSession has finished loading the map.</summary>
/// <remarks>
///     Marks the userSession's slot as loaded. When no slot that is still
///     <see cref="Basil.Domain.Multiplayer.SlotStatus.Playing" /> remains unloaded, a
///     <c>MatchAllPlayersLoaded</c> packet is broadcast to the match channel so the game can start the
///     map in sync. The read-mutate-broadcast sequence runs under the match's
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchLoadCompleteHandler(MatchMembershipService matchMembership) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchLoadComplete;

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

			slot.Loaded = true;

			var stillWaiting = match.Slots.Any(s => s is { Status: SlotStatus.Playing, Loaded: false });
			if (!stillWaiting) matchMembership.Enqueue(match, ServerPacketWriter.MatchAllPlayersLoaded(), false);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}