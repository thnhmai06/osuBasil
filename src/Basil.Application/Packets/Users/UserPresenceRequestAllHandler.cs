using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Users;

/// <summary>
///     Handles the UserPresenceRequestAll packet, which asks for the presence of every online userSession.
/// </summary>
/// <remarks>
///     The osu! client sends this variant only when more than 256 players are visible, asking for
///     the full online presence list at once. The body's leading 32-bit "ingame time" value is read
///     and discarded. A presence packet is then enqueued on the requester for every non-restricted
///     online session.
/// </remarks>
public sealed class UserPresenceRequestAllHandler(IUserSessionRegistry sessionRegistry) : IPacketHandler
{
	/// <summary>The <see cref="ClientPackets.UserPresenceRequestAll" /> packet type.</summary>
	public ClientPackets PacketId => ClientPackets.UserPresenceRequestAll;

	/// <summary>Restricted players may not request presence, so this handler is unavailable to them.</summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Enqueues a presence packet for every non-restricted online userSession on the requester.</summary>
	/// <param name="userSession">The userSession session that requested the presence data.</param>
	/// <param name="reader">The packet reader positioned at the UserPresenceRequestAll body.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A completed task.</returns>
	public Task HandleAsync(UserSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		reader.ReadI32(); // ingame_time, unused

		var buffer = new List<byte>();
		foreach (var other in sessionRegistry.All.Where(s => !s.Restricted))
			buffer.AddRange(PacketBuilders.BuildUserPresence(other));

		userSession.Enqueue([.. buffer]);
		return Task.CompletedTask;
	}
}