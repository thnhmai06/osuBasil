using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Spectating;
using Basil.Protocol.Game.Stable.Wire.Packets;

namespace Basil.Host.Stable.Packets;

internal sealed partial class PacketHandlers
{
	private async Task StartSpectatingAsync(StableSession osu, int hostId, CancellationToken ct)
	{
		if (await sender.Send(new FindStableClient(hostId), ct) is not { } host) return;

		await sender.Send(new SpectateUser(osu, host), ct);
	}

	private async Task StopSpectatingAsync(StableSession osu, CancellationToken ct)
	{
		if (registry.FindSpectating(osu) is { } host)
			await sender.Send(new StopSpectating(osu, host), ct);
	}

	private async Task ReportSpectatingFailedAsync(StableSession osu, CancellationToken ct)
	{
		if (registry.FindSpectating(osu) is { } host)
			await sender.Send(new ReportSpectatingFailed(osu, host), ct);
	}

	private async Task ForwardFramesAsync(StableSession osu, byte[] raw, CancellationToken ct)
	{
		if (osu.Spectators.Count == 0) return;
		outbox.Send(osu.Spectators, writer => writer.SpectateFrames(raw));
		if (!osu.HasApiSpectators) return;
		SpectatorBundle bundle;
		try
		{
			bundle = PacketReader.ForPayload(raw).ReadReplayFrameBundle();
		}
		catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException)
		{
			return; // A malformed bundle never breaks the relay.
		}

		await sender.Send(new PublishSpectatorFrames(osu, bundle), ct);
	}
}