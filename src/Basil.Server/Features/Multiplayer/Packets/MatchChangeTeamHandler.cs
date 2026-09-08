using Basil.Server.Shared.Http.Bancho;
using Basil.Server.Features.Multiplayer;
using Basil.Server.Shared.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Server.Features.Multiplayer.Packets;

/// <summary>Handles the client's request to switch their team in the match.</summary>
/// <remarks>
///     Toggles the userSession's slot team between <see cref="Basil.Domain.Multiplayer.MatchTeam.Red" /> and
///     <see cref="Basil.Domain.Multiplayer.MatchTeam.Blue" />, which only matters for team-vs.-team match
///     types. The updated state is broadcast to match members but not the lobby, so spectators of the
///     lobby do not see every mid-match team switch. The read-mutate-broadcast sequence runs under the
///     match's <see cref="Basil.Server.Features.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchChangeTeamHandler : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchChangeTeam;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = gameSession.Match;
		if (match is null) return;

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		var slot = match.GetSlot(gameSession.Id);
		if (slot is null) return;

		slot.Team = slot.Team == MatchTeam.Blue ? MatchTeam.Red : MatchTeam.Blue;
		mutation.PublishState(lobby: false);
	}
}