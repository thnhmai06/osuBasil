using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets;

/// <summary>
///     Handles a single client-to-server Bancho packet.
/// </summary>
/// <remarks>
///     Each <see cref="ClientPackets" /> value has exactly one handler implementation, matched by
///     <see cref="PacketId" />. The handler reads the packet body from the supplied
///     <see cref="PacketReader" /> and performs the corresponding server work.
/// </remarks>
public interface IPacketHandler
{
	/// <summary>The client packet type this handler is responsible for.</summary>
	ClientPackets PacketId { get; }

	/// <summary>Gets a value that indicates whether this handler may run for a restricted userSession.</summary>
	/// <value>
	///     <see langword="true" /> if the handler is included in the restricted-allowed packet set;
	///     otherwise, <see langword="false" />.
	/// </value>
	bool AllowedWhenRestricted { get; }

	/// <summary>Reads and processes one packet for the given userSession.</summary>
	/// <param name="gameSession">The game session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the body of this handler's packet type.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A task that completes when the packet has been fully handled.</returns>
	Task HandleAsync(GameSession gameSession, PacketReader reader, CancellationToken cancellationToken = default);
}