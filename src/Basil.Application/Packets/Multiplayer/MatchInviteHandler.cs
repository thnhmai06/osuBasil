using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the client's request to invite another userSession to the match.</summary>
/// <remarks>
///     Looks up the target by user id. When the inviter is in a match and the target is online, a
///     <c>MatchInvite</c> packet is enqueued for the target carrying the inviter's name, the match
///     embed, and the target's name. This is a pure relay: no match state is read or mutated, so the
///     match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" /> is not taken.
/// </remarks>
public sealed class MatchInviteHandler(ISessionRegistry<GameSession> sessionRegistry) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchInvite;

	public bool AllowedWhenRestricted => false;

	public Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var userId = reader.ReadI32();

		var match = gameSession.Match;
		if (match is null) return Task.CompletedTask;

		var target = sessionRegistry.GetByUserId(userId);

		target?.Enqueue(ServerPacketWriter.MatchInvite(gameSession.Id, gameSession.Name, match.Embed, target.Name));
		return Task.CompletedTask;
	}
}