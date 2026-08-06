using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the client's request to create a new multiplayer match.</summary>
/// <remarks>
///     Reads the match payload from the packet and validates it via
///     <see cref="MatchMembershipService.ValidateMatchData" /> before doing any work. Invalid payloads,
///     restricted players, and silenced players all get a <c>MatchJoinFail</c> response, with a
///     notification added for the latter two, and the request is dropped. The room is then created
///     through <see cref="MatchMembershipService.CreateAsync" />; if that returns <see langword="null" />
///     the userSession receives a <c>MatchJoinFail</c>. On success the creating userSession is added as the
///     match's first referee via <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.AddReferee" />.
///     No <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" /> is taken here: the room
///     does not exist yet, and <see cref="MatchMembershipService.CreateAsync" /> joins the host into it
///     under that lock internally.
/// </remarks>
public sealed class CreateMatchHandler(MatchMembershipService matchMembership) : IPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.CreateMatch;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: match creation is not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the create-match packet for the given userSession.</summary>
	/// <param name="userSession">The userSession session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the payload holding the match data.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public async Task HandleAsync(GameSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var matchData = reader.ReadMatch();
		if (!MatchMembershipService.ValidateMatchData(matchData, userSession.Id)) return;

		if (userSession.Restricted)
		{
			userSession.Enqueue([
				.. ServerPacketWriter.MatchJoinFail(),
				.. ServerPacketWriter.Notification("Multiplayer is not available while restricted.")
			]);
			return;
		}

		if (userSession.Silenced)
		{
			userSession.Enqueue([
				.. ServerPacketWriter.MatchJoinFail(),
				.. ServerPacketWriter.Notification("Multiplayer is not available while silenced.")
			]);
			return;
		}

		var match = await matchMembership.CreateAsync(userSession, matchData, cancellationToken: cancellationToken);
		match.AddReferee(userSession.Id);
	}
}