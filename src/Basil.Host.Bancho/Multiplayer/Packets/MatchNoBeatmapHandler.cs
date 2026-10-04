using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Users;
using Basil.Domain.Multiplayer.Runtime;
using Basil.Host.Bancho.Shared.Http;
using Basil.Protocol.Bancho.Packets;

namespace Basil.Host.Bancho.Multiplayer.Packets;

/// <summary>Handles the client's notification that the userSession does not have the match's beatmap.</summary>
/// <remarks>
///     Marks the userSession's slot as <see cref="RoomSlotStatus.NoMap" />, which
///     signals other clients that the userSession cannot play the selected map. The state update is
///     broadcast to match members but not the lobby. The read-mutate-broadcast sequence runs under the
///     match's <see cref="MatchSession.Lock" />.
/// </remarks>
public sealed class MatchNoBeatmapHandler(IUserCache userCache) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchNoBeatmap;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = gameSession.Match;
		if (match is null) return;

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		var slot = match.GetSlot(userCache.Resolve(gameSession));
		if (slot is null) return;

		slot.Status = RoomSlotStatus.NoMap;
		mutation.PublishState(false);
	}
}