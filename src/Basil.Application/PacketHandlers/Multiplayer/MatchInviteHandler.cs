using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Handles the client's request to invite another player to the match.</summary>
/// <remarks>
///     Looks up the target by user id. When the inviter is in a match and the target is online, a
///     <c>MatchInvite</c> packet is enqueued for the target carrying the inviter's name, the match
///     embed, and the target's name. This is a pure relay: no match state is read or mutated, so the
///     match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" /> is not taken.
/// </remarks>
public sealed class MatchInviteHandler(IPlayerSessionRegistry sessionRegistry) : IBanchoPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchInvite;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: invites are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the match-invite packet for the given player.</summary>
	/// <param name="player">The player session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the payload holding the target user id.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var userId = reader.ReadI32();

		var match = player.Match;
		if (match is null) return Task.CompletedTask;

		var target = sessionRegistry.GetById(userId);
		if (target is null) return Task.CompletedTask;

		target.Enqueue(ServerPacketWriter.MatchInvite(player.Id, player.Name, match.Embed, target.Name));
		return Task.CompletedTask;
	}
}