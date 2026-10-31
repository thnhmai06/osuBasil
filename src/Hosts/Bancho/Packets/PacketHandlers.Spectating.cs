using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Client;
using Basil.Protocol.Bancho.Models.Replay;
using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Host.Bancho.Packets;

internal sealed partial class PacketHandlers
{
	private async Task StartSpectatingAsync(BanchoConnection osu, int hostId, CancellationToken cancellationToken)
	{
		if (await FindUserAsync(hostId, cancellationToken) is not { } record ||
		    registry.OpenBanchoClient(record) is not { } host)
			return;

		await host.ScopeTo(osu).SpectateAsync();
	}

	/// <summary>Relays the bytes of a frame bundle to the spectators; decodes it once only when an API client watches.</summary>
	private void ForwardFrames(BanchoConnection osu, byte[] raw)
	{
		if (osu.Spectators.Count == 0) return;
		outbox.Send(osu.Spectators, writer => writer.SpectateFrames(raw));
		if (!osu.HasApiSpectators) return;
		ReplayFrameBundle bundle;
		try
		{
			bundle = PacketReader.ForPayload(raw).ReadReplayFrameBundle();
		}
		catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException)
		{
			return; // A malformed bundle never breaks the relay.
		}

		if (bundle.Action != ReplayAction.Standard) return;
		var score = bundle.ScoreFrame;
		osu.ScopeTo(osu).PublishFrames(new SpectatorBundle(SpectatorAction.Standard,
			[
				.. bundle.Frames.Select(frame => new SpectatorFrame((byte)frame.ButtonState, (byte)frame.TaikoByte,
					frame.X, frame.Y, frame.Time))
			],
			new SpectatorScore(score.Time, score.Id, score.Num300, score.Num100, score.Num50, score.NumGeki,
				score.NumKatu, score.NumMiss, score.TotalScore, score.MaxCombo, score.CurrentCombo, score.Perfect,
				score.CurrentHp), bundle.Sequence));
	}
}