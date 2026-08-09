using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Users;

/// <summary>
///     Handles the Logout packet, which disconnects the userSession from the server.
/// </summary>
/// <remarks>
///     The body's leading 32-bit integer is reserved and discarded. Every logout disconnects the
///     session: the player leaves any ongoing match, stops spectating, parts every joined channel,
///     and is removed from the online set.
/// </remarks>
public sealed class LogoutHandler(PlayerLogoutService logoutService)
	: IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.Logout;

	public bool AllowedWhenRestricted => true;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		reader.ReadI32(); // reserved

		//! Checked: THAT'S NOT TRUE.
		// ~~osu! tends to log out immediately after login (300-800ms observed)~~
		// ~~block any logout request within 1 second from login.~~

		await logoutService.LogoutAsync(gameSession, cancellationToken);
	}
}