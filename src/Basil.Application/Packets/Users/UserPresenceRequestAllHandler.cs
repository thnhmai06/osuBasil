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
public sealed class UserPresenceRequestAllHandler(ISessionRegistry<GameSession> sessionRegistry) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.UserPresenceRequestAll;

	public bool AllowedWhenRestricted => false;

	public Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		reader.ReadI32(); // ingame_time, unused

		var buffer = new List<byte>();
		foreach (var other in sessionRegistry.All.Where(s => !s.Restricted))
			buffer.AddRange(PacketBuilders.BuildUserPresence(other));

		gameSession.Enqueue([.. buffer]);
		return Task.CompletedTask;
	}
}