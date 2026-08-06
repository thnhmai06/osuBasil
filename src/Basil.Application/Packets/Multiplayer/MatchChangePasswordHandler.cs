using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the host's request to change the match's password.</summary>
/// <remarks>
///     Only the host may change the password. The new password is read from the client-supplied match
///     snapshot rather than as a standalone field, applied to the match, and the updated state is
///     broadcast to the match channel and the lobby. The read-mutate-broadcast sequence runs under the
///     match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchChangePasswordHandler(MatchMembershipService matchMembership) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchChangePassword;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var matchData = reader.ReadMatch();

		var match = gameSession.Match;
		if (!MatchMembershipService.ValidateMatchData(matchData, gameSession.Id) || match is null ||
		    gameSession.Id != match.HostId) return;

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			match.Password = matchData.Password;
			await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}