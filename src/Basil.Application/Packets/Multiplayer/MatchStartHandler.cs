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
	public ClientPackets PacketId => ClientPackets.MatchStart;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = gameSession.Match;
		if (match is null || gameSession.Id != match.HostId) return;

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