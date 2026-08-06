using System.Text.Json;
using Basil.Application.Formats;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Spectating;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Spectating;

/// <summary>
///     Handles the <see cref="ClientPackets.SpectateFrames" /> packet, which streams replay frame
///     data from a spectated userSession. Relays the packet's remaining bytes verbatim to every current
///     spectator, then decodes the same bytes into a structured <see cref="SpectateFramesEvent" />.
/// </summary>
/// <remarks>
///     The relay path is a raw forward of the packet's remaining bytes with no parsing, which suits
///     this packet's high sent rate. The structured decoding is a second, independent read of the same
///     buffer, not a change to the relayed packet; it follows the same pattern as
///     <see cref="Multiplayer.MatchScoreUpdateHandler" />'s scoreframe decode. The decoded event is
///     serialized with <see cref="BasilJsonOptions" /> and published to the userSession's live spectating
///     event channel, keyed by this userSession's id regardless of match membership. Decoding runs only
///     when <see cref="IPlayerInputEvents.HasSubscribers" /> is true, and a malformed bundle is
///     swallowed so it never breaks the relay above.
/// </remarks>
public sealed class SpectateFramesHandler(IPlayerInputEvents playerInputEvents) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.SpectateFrames;

	public bool AllowedWhenRestricted => false;

	public Task HandleAsync(GameSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var rawData = reader.ReadRaw(reader.RemainingLength);
		var packet = ServerPacketWriter.SpectateFrames(rawData);

		foreach (var spectator in userSession.Spectators)
			spectator.Enqueue(packet);

		if (!playerInputEvents.HasSubscribers)
			return Task.CompletedTask;

		try
		{
			var bundle = new PacketReader(rawData).ReadReplayFrameBundle();
			var user = new UserBrief(userSession.Id, userSession.Name, userSession.Country);
			var payload = JsonSerializer.SerializeToUtf8Bytes(
				new SpectateFramesEvent(user, bundle.Action, bundle.ExtraByte, bundle.Frames, bundle.ScoreFrame),
				BasilJsonOptions.Instance);
			playerInputEvents.PublishInput(userSession.Id, payload);
		}
		catch (Exception)
		{
			// A malformed/short bundle must never break the bancho relay above — the SSE channel
			// just misses this one update.
		}

		return Task.CompletedTask;
	}
}