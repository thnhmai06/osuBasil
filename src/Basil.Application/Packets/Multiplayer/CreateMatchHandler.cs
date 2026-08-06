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
	public ClientPackets PacketId => ClientPackets.CreateMatch;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var matchData = reader.ReadMatch();
		if (!MatchMembershipService.ValidateMatchData(matchData, gameSession.Id)) return;

		if (gameSession.Restricted)
		{
			gameSession.Enqueue([
				.. ServerPacketWriter.MatchJoinFail(),
				.. ServerPacketWriter.Notification("Multiplayer is not available while restricted.")
			]);
			return;
		}

		if (gameSession.Silenced)
		{
			gameSession.Enqueue([
				.. ServerPacketWriter.MatchJoinFail(),
				.. ServerPacketWriter.Notification("Multiplayer is not available while silenced.")
			]);
			return;
		}

		var match = await matchMembership.CreateAsync(gameSession, matchData, cancellationToken: cancellationToken);
		match?.AddReferee(gameSession.Id);
	}
}