using Basil.Domain;
using Basil.Application.Sessions.Objects;
using Basil.Protocol.Game.Stable.Wire.Packets;

namespace Basil.Host.Stable.Packets;

internal sealed class PacketDispatcher(PacketHandlers handlers, ILogger<PacketDispatcher> logger)
{
	public async Task DispatchAsync(Session actor, byte[] body, CancellationToken ct)
	{
		IReadOnlyList<ClientPacket> packets;
		try
		{
			packets = PacketReader.Split(body);
		}
		catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException)
		{
			logger.LogDebug(ex, "Malformed stable request of user {UserId}", actor.Record.Id);
			return;
		}

		foreach (var packet in packets)
			try
			{
				await handlers.HandleAsync(actor, packet, ct);
			}
			catch (BasilException ex)
			{
				logger.LogDebug(ex, "Stable packet {PacketType} of user {UserId} was not taken in", packet.Type,
					actor.Record.Id);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				logger.LogError(ex, "Stable packet {PacketType} of user {UserId} failed", packet.Type,
					actor.Record.Id);
			}
	}
}