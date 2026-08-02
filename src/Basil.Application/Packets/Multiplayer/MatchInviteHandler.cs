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
public sealed class MatchInviteHandler(IUserSessionRegistry sessionRegistry) : IPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchInvite;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: invites are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the match-invite packet for the given userSession.</summary>
	/// <param name="userSession">The userSession session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the payload holding the target user id.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public Task HandleAsync(UserSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var userId = reader.ReadI32();

		var match = userSession.Match;
		if (match is null) return Task.CompletedTask;

		var target = sessionRegistry.GetById(userId);

		target?.Enqueue(ServerPacketWriter.MatchInvite(userSession.Id, userSession.Name, match.Embed, target.Name));
		return Task.CompletedTask;
	}
}