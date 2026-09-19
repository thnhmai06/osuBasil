using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Users;
using Basil.Domain.Multiplayer.Runtime;
using Basil.Host.Bancho.Shared.Http;
using Basil.Protocol.Bancho.Packets;

namespace Basil.Host.Bancho.Multiplayer.Packets;

/// <summary>Handles the client's notification that the userSession has finished loading the map.</summary>
/// <remarks>
///     Marks the userSession's slot as loaded. When no slot that is still
///     <see cref="RoomSlotStatus.Playing" /> remains unloaded, a
///     <c>MatchAllPlayersLoaded</c> packet is broadcast to the match channel so the game can start the
///     map in sync. The read-mutate-broadcast sequence runs under the match's
///     <see cref="MatchSession.Lock" />.
/// </remarks>
public sealed class MatchLoadCompleteHandler(MatchBroadcast matchBroadcast, IUserCache userCache) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchLoadComplete;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = gameSession.Match;
		if (match is null) return;

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		var slot = match.GetSlot(userCache.Resolve(gameSession));
		if (slot is null) return;

		slot.BeatmapLoaded = true;

		var stillWaiting = match.Slots.Any(s => s is { Status: RoomSlotStatus.Playing, BeatmapLoaded: false });
		if (!stillWaiting) matchBroadcast.Enqueue(match, PacketWriter.MatchAllPlayersLoaded(), false);
	}
}