using System.Text.Json;
using Basil.Infrastructure.Shared.Eventing;
using Basil.Infrastructure.Shared.Http;
using Basil.Infrastructure.Shared.Http.Bancho;
using Basil.Infrastructure.Shared.Sessions;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Infrastructure.Multiplayer.Packets;

/// <summary>Handles the client's live score update during a match play.</summary>
/// <remarks>
///     MatchScoreUpdate arrives very frequently during a play, so the bancho relay stays a raw forward:
///     the payload bytes are re-wrapped into a <c>MatchScoreUpdate</c> server packet with the userSession's
///     slot id written into the wrapped header, then enqueued for the match channel without any parsing.
///     As a secondary, independent read of the same buffer, the frame is decoded into a
///     <see cref="ScoreFrame" /> and, when subscribers are connected,
///     published on the occupant's slot-scoped live score channel through
///     <see cref="ILiveEventHub" />. A malformed or short frame is swallowed so it can never break
///     the relay. Both reads happen while holding the match's
///     <see cref="MatchSession.Lock" />.
/// </remarks>
public sealed class MatchScoreUpdateHandler(MatchBroadcast matchBroadcast, ILiveEventHub hub)
	: IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchScoreUpdate;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var playData = reader.ReadRaw(reader.RemainingLength);

		var match = gameSession.Match;
		if (match is null) return;

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		var slotId = match.GetSlotId(gameSession.Id);
		if (slotId is null) return;

		// scorev2 adds an extra 8 bytes to play_data; either way, byte 11 (4 bytes into the
		// wrapped body) is overwritten with the slot id so clients can attribute the frame.
		var packet = PacketWriter.Wrap(ServerPackets.MatchScoreUpdate, playData);
		packet[11] = (byte)slotId.Value;

		matchBroadcast.Enqueue(match, packet, false);

		var scoreKey = MatchStreams.Score(match.DbId, slotId.Value);
		if (hub.HasSubscribers(scoreKey))
			try
			{
				var frame = new PacketReader(playData).ReadScoreFrame();
				var payload = JsonSerializer.SerializeToUtf8Bytes(
					BuildPlayerScore(gameSession, frame), BasilJsonOptions.Instance);
				hub.Publish(scoreKey, match.AllocateScoreVersion(), payload);
			}
			catch (Exception)
			{
				// A malformed or short scoreframe must never break the bancho relay above; the live
				// score channel just misses this one update.
			}
	}

	/// <summary>Builds the per-userSession live score payload for the SSE <c>/match/{id}/{playerName}</c> channel.</summary>
	/// <param name="userSession">The userSession whose score frame to broadcast.</param>
	/// <param name="frame">The decoded score frame from the client.</param>
	/// <returns>The <see cref="PlayerLiveScore" /> payload.</returns>
	private static PlayerLiveScore BuildPlayerScore(UserSession userSession, ScoreFrame frame)
	{
		return new PlayerLiveScore(
			new UserBrief(userSession.Id, userSession.Name, userSession.Country),
			frame.Time, frame.Num300, frame.Num100, frame.Num50, frame.NumGeki, frame.NumKatu,
			frame.NumMiss, frame.TotalScore, frame.MaxCombo, frame.CurrentCombo, frame.Perfect, frame.CurrentHp,
			frame.ScoreV2);
	}
}