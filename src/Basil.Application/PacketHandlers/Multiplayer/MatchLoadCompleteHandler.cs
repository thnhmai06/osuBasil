using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Handles the client's notification that the player has finished loading the map.</summary>
/// <remarks>
///     Marks the player's slot as loaded. When no slot that is still
///     <see cref="Basil.Domain.Multiplayer.SlotStatus.Playing" /> remains unloaded, a
///     <c>MatchAllPlayersLoaded</c> packet is broadcast to the match channel so the game can start the
///     map in sync. The read-mutate-broadcast sequence runs under the match's
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchLoadCompleteHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchLoadComplete;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: load notifications are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the load-complete packet for the given player.</summary>
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