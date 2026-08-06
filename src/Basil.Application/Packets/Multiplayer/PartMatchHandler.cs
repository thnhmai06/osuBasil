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
	public ClientPackets PacketId => ClientPackets.PartMatch;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = gameSession.Match;
		if (match is null) return;

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			await matchMembership.LeaveAsync(gameSession, match, cancellationToken);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}