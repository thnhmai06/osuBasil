using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the client's request to leave the match.</summary>
/// <remarks>
///     Delegates to <see cref="MatchMembershipService.LeaveAsync" />, which frees the userSession's slot,
///     removes them from the match channel, transfers the host when the leaving userSession was the host,
///     and tears the match down when no slots remain occupied, unless the room is a persistent one
///     created via <c>!mp make</c> or the HTTP API. The leave runs under the match's
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class PartMatchHandler(MatchMembershipService matchMembership) : IPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.PartMatch;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: leaving a match is not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the part-match packet for the given userSession.</summary>
	/// <param name="userSession">The userSession session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the start of the payload; this handler does not read the payload.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public async Task HandleAsync(GameSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = userSession.Match;
		if (match is null) return;

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			await matchMembership.LeaveAsync(userSession, match, cancellationToken);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}