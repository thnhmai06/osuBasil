using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the client's request to join an existing multiplayer match.</summary>
/// <remarks>
///     Reads the target match id and the optional password from the packet. A match not in the
///     registry, a restricted userSession, and a silenced userSession all get a <c>MatchJoinFail</c> response,
///     with a notification added for the latter two, and no further processing happens. Otherwise, the
///     match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" /> is acquired and
///     the join is delegated to <see cref="MatchMembershipService.JoinAsync" />, which validates the
///     password and assigns the slot, so slot allocation cannot race with other match packet handlers.
/// </remarks>
public sealed class JoinMatchHandler(IMatchRegistry matchRegistry, MatchMembershipService matchMembership)
	: IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.JoinMatch;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var matchId = reader.ReadI32();
		var password = reader.ReadString();

		var match = matchRegistry.GetById(matchId);
		if (match is null)
		{
			gameSession.Enqueue(ServerPacketWriter.MatchJoinFail());
			return;
		}

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

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			await matchMembership.JoinAsync(gameSession, match, password, cancellationToken);
		}
		finally
		{
			match.Lock.Release();
		}

		await matchMembership.EnqueueStateAsync(match, match.NextStateVersion(), cancellationToken: cancellationToken);
	}
}