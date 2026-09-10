using Basil.Server.Shared.Eventing;
using Basil.Server.Shared.Http.Bancho;
using System.Text.Json;
using Basil.Server.Shared.Http;
using Basil.Server.Features.Multiplayer;
using Basil.Server.Shared.Sessions;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Server.Features.Multiplayer.Packets;

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
///     <see cref="Basil.Server.Features.Multiplayer.MatchSession.Lock" />.
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
					MatchLiveSnapshotBuilder.BuildPlayerScore(gameSession, frame), BasilJsonOptions.Instance);
				hub.Publish(scoreKey, match.AllocateScoreVersion(), payload);
			}
			catch (Exception)
			{
				// A malformed or short scoreframe must never break the bancho relay above; the live
				// score channel just misses this one update.
			}
	}
}