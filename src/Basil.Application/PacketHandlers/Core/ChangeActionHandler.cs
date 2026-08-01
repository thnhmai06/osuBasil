using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Core;

/// <summary>
///     Handles the ChangeAction packet, which the client sends to report the player's current
///     activity, selected beatmap, and active mods.
/// </summary>
/// <remarks>
///     Reads the new <see cref="UserActivity" />, an info text, the beatmap md5, the active
///     <see cref="Mods" />, the <see cref="GameMode" />, and the beatmap id, then stores them all on
///     <see cref="PlayerSession.Status" />. When the player is not restricted, a rebuilt user-stats
///     packet is enqueued on every online session so the updated status reaches the other players'
///     friends lists and main menus immediately.
/// </remarks>
public sealed class ChangeActionHandler(IPlayerSessionRegistry sessionRegistry) : IBanchoPacketHandler
{
	/// <summary>The <see cref="ClientPackets.ChangeAction" /> packet type.</summary>
	public ClientPackets PacketId => ClientPackets.ChangeAction;

	/// <summary>Restricted players may change their action, so this handler is always available.</summary>
	public bool AllowedWhenRestricted => true;

	/// <summary>Reads the status fields from the packet and updates <see cref="PlayerSession.Status" />.</summary>
	/// <param name="player">The player session whose status is being updated.</param>
	/// <param name="reader">The packet reader positioned at the ChangeAction body.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A completed task.</returns>
	public Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var action = reader.ReadU8();
		var infoText = reader.ReadString();
		var mapMd5 = reader.ReadString();

		var mods = (Mods)reader.ReadU32();
		var mode = reader.ReadU8();
		var mapId = reader.ReadI32();

		player.Status.UserActivity = (UserActivity)action;
		player.Status.InfoText = infoText;
		player.Status.MapMd5 = mapMd5;
		player.Status.Mods = mods;
		player.Status.Mode = (GameMode)mode;
		player.Status.MapId = mapId;

		if (!player.Restricted)
		{
			var statsPacket = PacketBuilders.BuildUserStats(player);
			foreach (var other in sessionRegistry.All) other.Enqueue(statsPacket);
		}

		return Task.CompletedTask;
	}
}