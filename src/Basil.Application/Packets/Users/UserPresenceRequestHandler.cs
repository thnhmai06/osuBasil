using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Users;

/// <summary>
///     Handles the UserPresenceRequest packet, which asks for the presence of a specific set of
///     players.
/// </summary>
/// <remarks>
///     The body is an i16-counted list of 32-bit user ids. Each id is looked up in the session
///     registry and, when the target is online, a presence packet is enqueued on the requester.
///     Offline ids are skipped silently.
/// </remarks>
public sealed class UserPresenceRequestHandler(ISessionRegistry<GameSession> sessionRegistry) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.UserPresenceRequest;

	public bool AllowedWhenRestricted => false;

	public Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		foreach (var id in reader.ReadI32ListI16L())
		{
			var target = sessionRegistry.GetByUserId(id);
			if (target is not null) gameSession.Enqueue(PacketBuilders.BuildUserPresence(target));
		}

		return Task.CompletedTask;
	}
}