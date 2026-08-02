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
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchFailed;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: fail notifications are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the match-failed packet for the given userSession.</summary>
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
			var slotId = match.GetSlotId(userSession.Id);
			if (slotId is null) return;

			matchMembership.Enqueue(match, ServerPacketWriter.MatchPlayerFailed(slotId.Value), false);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}