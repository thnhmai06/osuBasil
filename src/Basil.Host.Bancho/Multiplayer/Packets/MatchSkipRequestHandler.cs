using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Users;
using Basil.Domain.Multiplayer.Runtime;
using Basil.Host.Bancho.Shared.Http;
using Basil.Protocol.Bancho.Packets;

namespace Basil.Host.Bancho.Multiplayer.Packets;

/// <summary>Handles the client's request to skip the map intro.</summary>
/// <remarks>
///     Marks the userSession's slot as skipped and broadcasts a <c>MatchPlayerSkipped</c> packet for the
///     userSession to the match channel. When every slot that is still
///     <see cref="RoomSlotStatus.Playing" /> has also skipped, a <c>MatchSkip</c>
///     packet is broadcast so the whole room skips in sync; that final packet excludes the lobby. The
///     read-mutate-broadcast sequence runs under the match's
///     <see cref="MatchSession.Lock" />.
/// </remarks>
public sealed class MatchSkipRequestHandler(MatchBroadcast matchBroadcast, IUserCache userCache) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchSkipRequest;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = gameSession.Match;
		if (match is null) return;

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		var slot = match.GetSlot(userCache.Resolve(gameSession));
		if (slot is null) return;

		slot.IntroSkipped = true;
		matchBroadcast.Enqueue(match, PacketWriter.MatchPlayerSkipped(gameSession.Id));

		var everyoneSkipped = match.Slots.All(s => s.Status != RoomSlotStatus.Playing || s.IntroSkipped);
		if (everyoneSkipped) matchBroadcast.Enqueue(match, PacketWriter.MatchSkip(), false);
	}
}