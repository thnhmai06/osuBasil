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
public sealed class UserPresenceRequestHandler(IUserSessionRegistry sessionRegistry) : IPacketHandler
{
	/// <summary>The <see cref="ClientPackets.UserPresenceRequest" /> packet type.</summary>
	public ClientPackets PacketId => ClientPackets.UserPresenceRequest;

	/// <summary>Restricted players may not request presence, so this handler is unavailable to them.</summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Enqueues a presence packet for each requested user id that resolves to an online session.</summary>
	/// <param name="userSession">The userSession session that requested the presence data.</param>
	/// <param name="reader">The packet reader positioned at the UserPresenceRequest body.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A completed task.</returns>
	public Task HandleAsync(UserSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		foreach (var id in reader.ReadI32ListI16L())
		{
			var target = sessionRegistry.GetById(id);
			if (target is not null) userSession.Enqueue(PacketBuilders.BuildUserPresence(target));
		}

		return Task.CompletedTask;
	}
}