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

		await host.ScopeTo(osu).SpectateAsync();
	}

	/// <summary>Relays the bytes of a frame bundle to the spectators; decodes it once only when an API client watches.</summary>
	private void ForwardFrames(StableConnection osu, byte[] raw)
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
		osu.ScopeTo(osu).PublishFrames(bundle);
	}
}