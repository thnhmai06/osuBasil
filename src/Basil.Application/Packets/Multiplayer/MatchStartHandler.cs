using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the host's request to start the match.</summary>
/// <remarks>
///     Only the host may start the match. The start logic itself lives in
///     <see cref="MatchMembershipService.StartAsync" />, which marks every non-NoMap slot as Playing,
///     creates the round record, and broadcasts the start packets; that method is shared with
///     <c>!mp start</c> and <c>!mp force</c>. The call runs under the match's
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchStartHandler(MatchMembershipService matchMembership) : IPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchStart;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: match starts are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the match-start packet for the given userSession.</summary>
	/// <param name="userSession">The userSession session that sent the packet.</param>
	/// <param name="reader">
	///     The packet reader positioned at the start of the payload; this handler does not read the payload.
	/// </param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public async Task HandleAsync(GameSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = userSession.Match;
		if (match is null || userSession.Id != match.HostId) return;

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			await matchMembership.StartAsync(match, cancellationToken);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}