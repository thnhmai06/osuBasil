using Basil.Server.Shared.Http.Bancho;
using Basil.Server.Features.Multiplayer;
using Basil.Server.Shared.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Server.Features.Multiplayer.Packets;

/// <summary>Handles the client's request to leave the match.</summary>
/// <remarks>
///     Delegates to <see cref="MatchMembership.LeaveAsync" />, which frees the userSession's slot,
///     removes them from the match channel, transfers the host when the leaving userSession was the host,
///     and tears the match down when no slots remain occupied, unless the room is a persistent one
///     created via <c>!mp make</c> or the HTTP API. The leave runs under the match's
///     <see cref="Basil.Server.Features.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class PartMatchHandler(MatchMembership matchMembership) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.PartMatch;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = gameSession.Match;
		if (match is null) return;

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		await matchMembership.LeaveAsync(gameSession, match, cancellationToken);
		mutation.PublishState();
	}
}
