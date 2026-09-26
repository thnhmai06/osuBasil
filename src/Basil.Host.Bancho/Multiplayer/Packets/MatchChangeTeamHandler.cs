using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Users;
using Basil.Domain.Multiplayer.Records;
using Basil.Host.Bancho.Shared.Http;
using Basil.Protocol.Bancho.Packets;

namespace Basil.Host.Bancho.Multiplayer.Packets;

/// <summary>Handles the client's request to switch their team in the match.</summary>
/// <remarks>
///     Toggles the userSession's slot team between <see cref="MatchTeam.Red" /> and
///     <see cref="MatchTeam.Blue" />, which only matters for team-vs.-team match
///     types. The updated state is broadcast to match members but not the lobby, so spectators of the
///     lobby do not see every mid-match team switch. The read-mutate-broadcast sequence runs under the
///     match's <see cref="MatchSession.Lock" />.
/// </remarks>
public sealed class MatchChangeTeamHandler(IUserCache userCache) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchChangeTeam;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = gameSession.Match;
		if (match is null) return;

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		var slot = match.GetSlot(userCache.Resolve(gameSession));
		if (slot is null) return;

		slot.Team = slot.Team == MatchTeam.Blue ? MatchTeam.Red : MatchTeam.Blue;
		mutation.PublishState(false);
	}
}