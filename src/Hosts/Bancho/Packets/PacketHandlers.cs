using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Chat.Runtime;
using Basil.Application.Content.Persistence;
using Basil.Application.Multiplayer.Runtime;
using Basil.Application.Scores.Persistence;
using Basil.Application.Users.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Host.Bancho.Support;
using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Host.Bancho.Packets;

/// <summary>Handles the packets an osu! client sends; each handler works through the scopes of the sender's connection.</summary>
internal sealed partial class PacketHandlers(
	UserRegistry registry,
	Lobby lobby,
	ChannelResolver channels,
	PmChannels pmChannels,
	IUserRepository users,
	IUserStatsRepository statsRepository,
	IBeatmapRepository beatmaps,
	ISettingsRepository settings,
	Outbox outbox,
	RateLimiter limiter,
	TimeProvider clock)
{
	/// <summary>Handles one packet from a connection.</summary>
	/// <param name="by">The connection that sent it.</param>
	/// <param name="packet">The packet.</param>
	/// <param name="cancellationToken">A token that cancels the handling.</param>
	public Task HandleAsync(Connection by, ClientPacket packet, CancellationToken cancellationToken)
	{
		var reader = PacketReader.ForPayload(packet.Payload);
		return packet.Type switch
		{
			ClientPacketType.Logout => Run(() => registry.Close(by, SessionEndReason.LoggedOut)),
			ClientPacketType.UserStatsRequest => StatsRequestAsync(by, reader.ReadInt32List(), cancellationToken),
			ClientPacketType.UserPresenceRequest => PresenceRequestAsync(by, reader.ReadInt32List(), cancellationToken),
			ClientPacketType.UserPresenceRequestAll => PresenceRequestAsync(by,
				registry.Online.Keys.Select(user => user.Id).ToList(), cancellationToken),
			ClientPacketType.TournamentMatchInfoRequest => TourneyInfoAsync(by, reader.ReadInt32(), cancellationToken),
			ClientPacketType.TournamentJoinMatchChannel => TourneyJoinAsync(by, reader.ReadInt32(), cancellationToken),
			ClientPacketType.TournamentLeaveMatchChannel => TourneyLeaveAsync(by, reader.ReadInt32(), cancellationToken),
			_ when by is BanchoConnection osu => HandleOsuAsync(osu, packet.Type, packet.Payload, reader, cancellationToken),
			_ => Task.CompletedTask
		};
	}

	private Task HandleOsuAsync(BanchoConnection osu, ClientPacketType type, byte[] payload, PacketReader reader,
		CancellationToken cancellationToken)
	{
		return type switch
		{
			ClientPacketType.ChangeAction => ChangeActionAsync(osu, reader),
			ClientPacketType.RequestStatusUpdate => StatsRequestAsync(osu, [osu.Record.Id], cancellationToken),
			ClientPacketType.FriendAdd => FriendAsync(osu, reader.ReadInt32(), true, cancellationToken),
			ClientPacketType.FriendRemove => FriendAsync(osu, reader.ReadInt32(), false, cancellationToken),
			ClientPacketType.SetAwayMessage => Run(() => SetAway(osu, reader.ReadMessage().Text)),
			ClientPacketType.ToggleBlockNonFriendDms => Run(() =>
				osu.User.ScopeTo(osu).Profile.SetPmPrivate(reader.ReadInt32() == 1)),
			ClientPacketType.BeatmapInfoRequest => BeatmapInfoAsync(osu, reader.ReadBeatmapInfoRequest(), cancellationToken),
			ClientPacketType.ChannelJoin => Run(() => JoinChannel(osu, reader.ReadOsuString())),
			ClientPacketType.ChannelPart => Run(() => PartChannel(osu, reader.ReadOsuString())),
			ClientPacketType.SendPublicMessage => PublicMessageAsync(osu, reader.ReadMessage(), cancellationToken),
			ClientPacketType.SendPrivateMessage => PrivateMessageAsync(osu, reader.ReadMessage(), cancellationToken),
			ClientPacketType.JoinLobby => Run(() => lobby.ScopeTo(osu).Watch()),
			ClientPacketType.PartLobby => Run(() => lobby.ScopeTo(osu).Unwatch()),
			ClientPacketType.StartSpectating => StartSpectatingAsync(osu, reader.ReadInt32(), cancellationToken),
			ClientPacketType.StopSpectating => Run(() => registry.FindSpectating(osu)?.ScopeTo(osu).StopSpectating()),
			ClientPacketType.CantSpectate => Run(() => registry.FindSpectating(osu)?.ScopeTo(osu).ReportSpectatingFailed()),
			ClientPacketType.SpectateFrames => Run(() => ForwardFrames(osu, payload)),
			_ => HandleRoomAsync(osu, type, payload, reader, cancellationToken)
		};
	}

	private static Task Run(Action action)
	{
		action();
		return Task.CompletedTask;
	}

	private async Task<UserRecord?> FindUserAsync(int id, CancellationToken cancellationToken)
	{
		return await users.GetAsync(id, cancellationToken);
	}

	private async Task ChangeActionAsync(BanchoConnection osu, PacketReader reader)
	{
		var action = reader.ReadChangeAction();
		Md5? hash = null;
		if (action.MapMd5.Length == 32)
			try
			{
				hash = new Md5(action.MapMd5);
			}
			catch (ArgumentException)
			{
			}

		if (!Enum.IsDefined((UserActivity)action.Action) || !Enum.IsDefined((GameMode)action.Mode)) return;
		var status = new PlayerStatus((UserActivity)action.Action, action.InfoText, hash,
			action.MapId == 0 ? null : action.MapId, (GameMods)action.Mods, (GameMode)action.Mode);
		osu.ScopeTo(osu).SetStatus(status);
		await Task.CompletedTask;
	}

	private async Task StatsRequestAsync(Connection by, IEnumerable<int> ids, CancellationToken cancellationToken)
	{
		var wanted = new List<User>();
		foreach (var id in ids)
			if (registry.Online.FirstOrDefault(pair => pair.Key.Id == id) is { Value: { } online })
				wanted.Add(online);

		var stats = new List<UserStats>();
		foreach (var user in wanted)
			stats.Add(await statsRepository.GetAsync(user.Record, user.Bancho?.Status.GameMode ?? GameMode.Standard,
				cancellationToken));
		outbox.Send(by, writer =>
		{
			for (var index = 0; index < wanted.Count; index++) BanchoView.WriteStats(writer, wanted[index], stats[index]);
		});
	}

	private async Task PresenceRequestAsync(Connection by, IEnumerable<int> ids, CancellationToken cancellationToken)
	{
		var wanted = new List<User>();
		foreach (var id in ids)
			if (registry.Online.FirstOrDefault(pair => pair.Key.Id == id) is { Value: { } online })
				wanted.Add(online);

		var now = clock.GetUtcNow();
		var stats = new List<UserStats>();
		foreach (var user in wanted)
			stats.Add(await statsRepository.GetAsync(user.Record, user.Bancho?.Status.GameMode ?? GameMode.Standard,
				cancellationToken));
		outbox.Send(by, writer =>
		{
			for (var index = 0; index < wanted.Count; index++)
				BanchoView.WritePresence(writer, wanted[index], stats[index], now);
		});
	}

	private async Task FriendAsync(BanchoConnection osu, int id, bool add, CancellationToken cancellationToken)
	{
		if (await FindUserAsync(id, cancellationToken) is not { } target) return;
		var scoped = registry.Of(target).ScopeTo(osu).Relationships;
		if (add) await scoped.AddFriendAsync(cancellationToken);
		else await scoped.RemoveFriendAsync(cancellationToken);
	}

	private static void SetAway(BanchoConnection osu, string text)
	{
		osu.User.ScopeTo(osu).Profile.SetAway(string.IsNullOrEmpty(text) ? null : text);
	}

	private async Task BeatmapInfoAsync(BanchoConnection osu, Protocol.Bancho.Models.Beatmaps.BeatmapInfoRequest request,
		CancellationToken cancellationToken)
	{
		var entries = new List<Protocol.Bancho.Models.Beatmaps.BeatmapInfoReplyEntry>();
		for (var index = 0; index < request.Filenames.Count; index++)
		{
			var page = await beatmaps.ListAsync(new Basil.Application.Beatmaps.Persistence.BeatmapQuery(
				FileName: request.Filenames[index]), new Basil.Application.Common.PageRequest(0, 1), cancellationToken);
			if (page.Items.FirstOrDefault() is { } map)
				entries.Add(new Protocol.Bancho.Models.Beatmaps.BeatmapInfoReplyEntry((short)index, map.Id,
					map.Beatmapset.Id, 0, (byte)map.Beatmapset.Status, -1, -1, -1, -1, map.Hash.ToString()));
		}

		outbox.Send(osu, writer => writer.BeatmapInfoReply(entries));
	}
}
