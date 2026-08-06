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
public sealed class ChangeActionHandler(ISessionRegistry<GameSession> sessionRegistry) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.ChangeAction;

	public bool AllowedWhenRestricted => true;

	public Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var action = reader.ReadU8();
		var infoText = reader.ReadString();
		var mapMd5 = reader.ReadString();

		var mods = (Mods)reader.ReadU32();
		var mode = reader.ReadU8();
		var mapId = reader.ReadI32();

		gameSession.Status.UserActivity = (UserActivity)action;
		gameSession.Status.InfoText = infoText;
		gameSession.Status.MapMd5 = mapMd5;
		gameSession.Status.Mods = mods;
		gameSession.Status.Mode = (GameMode)mode;
		gameSession.Status.MapId = mapId;

		if (gameSession.Restricted) return Task.CompletedTask;

		var statsPacket = PacketBuilders.BuildUserStats(gameSession);
		foreach (var other in sessionRegistry.All)
			other.Enqueue(statsPacket);

		return Task.CompletedTask;
	}
}