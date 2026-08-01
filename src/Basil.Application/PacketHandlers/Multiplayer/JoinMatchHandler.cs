using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Handles the client's request to join an existing multiplayer match.</summary>
/// <remarks>
///     Reads the target match id and the optional password from the packet. A match that is not in the
///     registry, a restricted player, and a silenced player all get a <c>MatchJoinFail</c> response,
///     with a notification added for the latter two, and no further processing happens. Otherwise the
///     match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" /> is acquired and
///     the join is delegated to <see cref="MatchMembershipService.JoinAsync" />, which validates the
///     password and assigns the slot, so slot allocation cannot race with other match packet handlers.
/// </remarks>
public sealed class JoinMatchHandler(IMatchRegistry matchRegistry, MatchMembershipService matchMembership)
	: IBanchoPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.JoinMatch;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: match joins are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the join-match packet for the given player.</summary>
	/// <param name="player">The player session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the payload holding the match id and password.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var matchId = reader.ReadI32();
		var password = reader.ReadString();

		var match = matchRegistry.GetById(matchId);
		if (match is null)
		{
			player.Enqueue(ServerPacketWriter.MatchJoinFail());
			return;
		}

		if (player.Restricted)
		{
			player.Enqueue([
				.. ServerPacketWriter.MatchJoinFail(),
				.. ServerPacketWriter.Notification("Multiplayer is not available while restricted.")
			]);
			return;
		}

		if (player.Silenced)
		{
			player.Enqueue([
				.. ServerPacketWriter.MatchJoinFail(),
				.. ServerPacketWriter.Notification("Multiplayer is not available while silenced.")
			]);
			return;
		}

		await match.Lock.WaitAsync();
		try
		{
			await matchMembership.JoinAsync(player, match, password, cancellationToken);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}