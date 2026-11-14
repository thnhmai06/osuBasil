using Basil.Application.Users.Objects.Connections;
using Basil.Protocol.Stable.Wire.Packets;

namespace Basil.Host.Stable.Packets;

/// <summary>Runs the packets of a client request in order, so one bad packet cannot spoil the rest.</summary>
internal sealed class PacketDispatcher(PacketHandlers handlers, ILogger<PacketDispatcher> logger)
{
	/// <summary>Handles every packet of a request body for a connection.</summary>
	/// <param name="actor">The connection that sent the body.</param>
	/// <param name="body">The raw request body.</param>
	/// <param name="ct">A token that cancels the handling.</param>
	public async Task DispatchAsync(Connection actor, byte[] body, CancellationToken ct)
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
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				logger.LogError(ex, "Stable packet {PacketType} of user {UserId} failed", packet.Type,
					actor.Record.Id);
			}
	}
}