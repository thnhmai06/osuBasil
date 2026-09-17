using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Users;
using Basil.Domain.Multiplayer.Runtime;
using Basil.Host.Bancho.Shared.Http;
using Basil.Protocol.Packets;

namespace Basil.Host.Bancho.Multiplayer.Packets;

/// <summary>Handles the client's notification that the userSession is ready.</summary>
/// <remarks>
///     Marks the userSession's slot as <see cref="RoomSlotStatus.Ready" />. The state
///     update is broadcast to match members but not the lobby. The read-mutate-broadcast sequence runs
///     under the match's <see cref="MatchSession.Lock" />.
/// </remarks>
public sealed class MatchReadyHandler(IUserCache userCache) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchReady;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = gameSession.Match;
		if (match is null) return;

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		var slot = match.GetSlot(userCache.Resolve(gameSession));
		if (slot is null) return;

		slot.Status = RoomSlotStatus.Ready;
		mutation.PublishState(false);
	}
}