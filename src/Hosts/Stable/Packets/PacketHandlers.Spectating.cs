using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Client;
using Basil.Protocol.Stable.Wire.Packets;

namespace Basil.Host.Stable.Packets;

internal sealed partial class PacketHandlers
{
	private async Task StartSpectatingAsync(StableConnection osu, int hostId, CancellationToken cancellationToken)
	{
		if (await FindUserAsync(hostId, cancellationToken) is not { } record ||
		    registry.OpenStableClient(record) is not { } host)
			return;

		await sender.Send(new SpectateUser(osu, host), cancellationToken);
	}

	private async Task StopSpectatingAsync(StableConnection osu, CancellationToken cancellationToken)
	{
		if (registry.FindSpectating(osu) is { } host) await sender.Send(new StopSpectating(osu, host), cancellationToken);
	}

	private async Task ReportSpectatingFailedAsync(StableConnection osu, CancellationToken cancellationToken)
	{
		if (registry.FindSpectating(osu) is { } host)
			await sender.Send(new ReportSpectatingFailed(osu, host), cancellationToken);
	}

	/// <summary>Relays the bytes of a frame bundle to the spectators; decodes it once only when an API client watches.</summary>
	private async Task ForwardFramesAsync(StableConnection osu, byte[] raw, CancellationToken cancellationToken)
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

		if (bundle.Action != SpectatorAction.Standard) return;
		await sender.Send(new PublishSpectatorFrames(osu, bundle), cancellationToken);
	}
}