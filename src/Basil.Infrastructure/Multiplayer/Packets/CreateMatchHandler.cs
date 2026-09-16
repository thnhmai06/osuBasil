using Basil.Infrastructure.Shared.Http.Bancho;
using Basil.Infrastructure.Shared.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Infrastructure.Multiplayer.Packets;

/// <summary>Handles the client's request to create a new multiplayer match.</summary>
/// <remarks>
///     Reads the match payload from the packet and validates it via
///     <see cref="MatchCreationDataMapper.IsValid" /> before doing any work. Invalid payloads,
///     restricted players, and silenced players all get a <c>MatchJoinFail</c> response, with a
///     notification added for the latter two, and the request is dropped. The room is then created
///     through <see cref="MatchLifecycle.CreateAsync" />; if that returns <see langword="null" />
///     the userSession receives a <c>MatchJoinFail</c>. On success the creating userSession is added as the
///     match's first referee via <see cref="MatchSession.AddReferee" />.
///     <see cref="MatchLifecycle.CreateAsync" /> joins the host into the room under the match's
///     <see cref="MatchSession.Lock" />, so no lock is taken here.
/// </remarks>
public sealed class CreateMatchHandler(MatchLifecycle matchLifecycle) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.CreateMatch;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var matchData = reader.ReadMatch();
		if (!MatchCreationDataMapper.IsValid(matchData, gameSession.Id)) return;

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

		var match = await matchLifecycle.CreateAsync(gameSession, matchData.ToCreationData(), cancellationToken);
		match?.AddReferee(gameSession.Id);
	}
}