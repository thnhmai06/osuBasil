using Basil.Server.Shared.Http.Bancho;
using Basil.Server.Features.Multiplayer;
using Basil.Server.Shared.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Server.Features.Multiplayer.Packets;

/// <summary>Handles the client's notification that the userSession is no longer ready.</summary>
/// <remarks>
///     Marks the userSession's slot as <see cref="Basil.Domain.Multiplayer.SlotStatus.NotReady" />. The
///     state update is broadcast to match members but not the lobby. The read-mutate-broadcast sequence
///     runs under the match's <see cref="Basil.Server.Features.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchNotReadyHandler : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchNotReady;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = gameSession.Match;
		if (match is null) return;

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		var slot = match.GetSlot(gameSession.Id);
		if (slot is null) return;

		slot.Status = SlotStatus.NotReady;
		mutation.PublishState(lobby: false);
	}
}