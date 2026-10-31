using Basil.Application.Users.Objects.Connections;
using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Host.Bancho.Packets;

/// <summary>Runs the packets of a client request in order, so one bad packet cannot spoil the rest.</summary>
internal sealed class PacketDispatcher(PacketHandlers handlers, ILogger<PacketDispatcher> logger)
{
	/// <summary>Handles every packet of a request body for a connection.</summary>
	/// <param name="by">The connection that sent the body.</param>
	/// <param name="body">The raw request body.</param>
	/// <param name="cancellationToken">A token that cancels the handling.</param>
	public async Task DispatchAsync(Connection by, byte[] body, CancellationToken cancellationToken)
	{
		IReadOnlyList<ClientPacket> packets;
		try
		{
			packets = PacketReader.Split(body);
		}
		catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException)
		{
			logger.LogDebug(ex, "Malformed bancho request of user {UserId}", by.Record.Id);
			return;
		}

		foreach (var packet in packets)
			try
			{
				await handlers.HandleAsync(by, packet, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				logger.LogError(ex, "Bancho packet {PacketType} of user {UserId} failed", packet.Type, by.Record.Id);
			}
	}
}
