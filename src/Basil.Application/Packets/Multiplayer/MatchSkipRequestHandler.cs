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
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchSkipRequest;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: skip requests are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the skip-request packet for the given userSession.</summary>
	/// <param name="userSession">The userSession session that sent the packet.</param>
	/// <param name="reader">
	///		The packet reader positioned at the start of the payload; this handler does not read the payload.
	/// </param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public async Task HandleAsync(UserSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = userSession.Match;
		if (match is null) return;

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			var slot = match.GetSlot(userSession.Id);
			if (slot is null) return;

			slot.Skipped = true;
			matchMembership.Enqueue(match, ServerPacketWriter.MatchPlayerSkipped(userSession.Id));

			var everyoneSkipped = match.Slots.All(s => s.Status != SlotStatus.Playing || s.Skipped);
			if (everyoneSkipped) matchMembership.Enqueue(match, ServerPacketWriter.MatchSkip(), false);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}