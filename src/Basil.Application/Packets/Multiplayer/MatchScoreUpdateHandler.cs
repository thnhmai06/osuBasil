using System.Text.Json;
using Basil.Application.Formats;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the client's live score update during a match play.</summary>
/// <remarks>
///     MatchScoreUpdate arrives very frequently during a play, so the bancho relay stays a raw forward:
///     the payload bytes are re-wrapped into a <c>MatchScoreUpdate</c> server packet with the userSession's
///     slot id written into the wrapped header, then enqueued for the match channel without any parsing.
///     As a secondary, independent read of the same buffer, the frame is decoded into a
///     <see cref="ScoreFrame" /> and, when subscribers are connected,
///     published on the userSession's live score channel through
///     <see cref="IMatchLiveEvents" />. A malformed or short frame is swallowed so it can never break
///     the relay. Both reads happen while holding the match's
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchScoreUpdateHandler(MatchMembershipService matchMembership, IMatchLiveEvents eventBus)
	: IPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchScoreUpdate;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: score updates are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the score-update packet for the given userSession.</summary>
	/// <param name="userSession">The userSession session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the payload holding the raw score frame bytes.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public async Task HandleAsync(GameSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var playData = reader.ReadRaw(reader.RemainingLength);

		var match = userSession.Match;
		if (match is null) return;

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			var slotId = match.GetSlotId(userSession.Id);
			if (slotId is null) return;

			// scorev2 adds an extra 8 bytes to play_data; either way, byte 11 (4 bytes into the
			// wrapped body) is overwritten with the slot id so clients can attribute the frame.
			var packet = PacketWriter.Wrap(ServerPackets.MatchScoreUpdate, playData);
			packet[11] = (byte)slotId.Value;

			matchMembership.Enqueue(match, packet, false);

			if (eventBus.HasPlayerScoreSubscribers)
				try
				{
					var frame = new PacketReader(playData).ReadScoreFrame();
					var payload = JsonSerializer.SerializeToUtf8Bytes(
						MatchLiveSnapshotBuilder.BuildPlayerScore(userSession, frame), BasilJsonOptions.Instance);
					eventBus.PublishPlayer(match.DbId, userSession.Name, payload);
				}
				catch (Exception)
				{
					// A malformed or short scoreframe must never break the bancho relay above; the live
					// score channel just misses this one update.
				}
		}
		finally
		{
			match.Lock.Release();
		}
	}
}