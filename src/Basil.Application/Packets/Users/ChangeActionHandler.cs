using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Users;

/// <summary>
///     Handles the ChangeAction packet, which the client sends to report the userSession's current
///     activity, selected beatmap, and active mods.
/// </summary>
/// <remarks>
///     Reads the new <see cref="UserActivity" />, an info text, the beatmap md5, the active
///     <see cref="Mods" />, the <see cref="GameMode" />, and the beatmap id, then stores them all on
///     <see cref="UserSession.Status" />. When the userSession is not restricted, a rebuilt user-stats
///     packet is enqueued on every online session so the updated status reaches the other players'
///     friends lists and main menus immediately.
/// </remarks>
public sealed class ChangeActionHandler(IUserSessionRegistry sessionRegistry) : IPacketHandler
{
	/// <summary>The <see cref="ClientPackets.ChangeAction" /> packet type.</summary>
	public ClientPackets PacketId => ClientPackets.ChangeAction;

	/// <summary>Restricted players may change their action, so this handler is always available.</summary>
	public bool AllowedWhenRestricted => true;

	/// <summary>Reads the status fields from the packet and updates <see cref="UserSession.Status" />.</summary>
	/// <param name="userSession">The userSession session whose status is being updated.</param>
	/// <param name="reader">The packet reader positioned at the ChangeAction body.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A completed task.</returns>
	public Task HandleAsync(GameSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var action = reader.ReadU8();
		var infoText = reader.ReadString();
		var mapMd5 = reader.ReadString();

		var mods = (Mods)reader.ReadU32();
		var mode = reader.ReadU8();
		var mapId = reader.ReadI32();

		userSession.Status.UserActivity = (UserActivity)action;
		userSession.Status.InfoText = infoText;
		userSession.Status.MapMd5 = mapMd5;
		userSession.Status.Mods = mods;
		userSession.Status.Mode = (GameMode)mode;
		userSession.Status.MapId = mapId;

		if (userSession.Restricted) return Task.CompletedTask;

		var statsPacket = PacketBuilders.BuildUserStats(userSession);
		foreach (var other in sessionRegistry.GameSessions)
			other.Enqueue(statsPacket);

		return Task.CompletedTask;
	}
}