using Basil.Server.Shared.Http.Bancho;
using Basil.Server.Features.Multiplayer;
using Basil.Server.Shared.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Server.Features.Multiplayer.Packets;

/// <summary>Handles the client's notification that the userSession does not have the match's beatmap.</summary>
/// <remarks>
///     Marks the userSession's slot as <see cref="Basil.Domain.Multiplayer.SlotStatus.NoMap" />, which
///     signals other clients that the userSession cannot play the selected map. The state update is
///     broadcast to match members but not the lobby. The read-mutate-broadcast sequence runs under the
///     match's <see cref="Basil.Server.Features.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchNoBeatmapHandler(MatchMembershipService matchMembership) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchNoBeatmap;

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

			slot.Status = SlotStatus.NoMap;
			version = match.NextStateVersion();
		}
		finally
		{
			match.Lock.Release();
		}

		await matchMembership.EnqueueStateAsync(match, version, false, cancellationToken);
	}
}