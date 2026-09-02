using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the client's request to switch their team in the match.</summary>
/// <remarks>
///     Toggles the userSession's slot team between <see cref="Basil.Domain.Multiplayer.MatchTeam.Red" /> and
///     <see cref="Basil.Domain.Multiplayer.MatchTeam.Blue" />, which only matters for team-vs.-team match
///     types. The updated state is broadcast to match members but not the lobby, so spectators of the
///     lobby do not see every mid-match team switch. The read-mutate-broadcast sequence runs under the
///     match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchChangeTeamHandler(MatchMembershipService matchMembership) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchChangeTeam;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = gameSession.Match;
		if (match is null) return;

		await match.Lock.WaitAsync(cancellationToken);
		long version;
		try
		{
			var slot = match.GetSlot(gameSession.Id);
			if (slot is null) return;

			slot.Team = slot.Team == MatchTeam.Blue ? MatchTeam.Red : MatchTeam.Blue;
			version = match.NextStateVersion();
		}
		finally
		{
			match.Lock.Release();
		}

		await matchMembership.EnqueueStateAsync(match, version, false, cancellationToken);
	}
}