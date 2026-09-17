using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Users;
using Basil.Host.Bancho.Shared.Http;
using Basil.Protocol.Packets;

namespace Basil.Host.Bancho.Multiplayer.Packets;

/// <summary>Handles the client's notification that the userSession has failed the current map.</summary>
/// <remarks>
///     Relays the failure to the match channel as a <c>MatchPlayerFailed</c> packet carrying the
///     userSession's slot id, so other clients can render the failure indicator for that slot. Slot state is
///     not changed. The relay runs under the match's
///     <see cref="MatchSession.Lock" />.
/// </remarks>
public sealed class MatchFailedHandler(MatchBroadcast matchBroadcast, IUserCache userCache) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchFailed;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = gameSession.Match;
		if (match is null) return;

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		var slotId = match.GetSlotId(userCache.Resolve(gameSession));
		if (slotId is null) return;

		matchBroadcast.Enqueue(match, ServerPacketWriter.MatchPlayerFailed(slotId.Value), false);
	}
}