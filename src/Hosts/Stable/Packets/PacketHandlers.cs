using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Scores.Persistence;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Gateway;
using Basil.Host.Stable.Support;
using Basil.Protocol.Stable.Models.Beatmaps;
using Basil.Protocol.Stable.Wire.Packets;
using Mediator;

namespace Basil.Host.Stable.Packets;

/// <summary>Handles the packets an osu! client sends; each handler works through the scopes of the sender's connection.</summary>
internal sealed partial class PacketHandlers(
	UserRegistry registry,
	Lobby lobby,
	ChannelResolver channels,
	ISender sender,
	IUserRepository users,
	IUserStatsRepository statsRepository,
	IBeatmapRepository beatmaps,
	ISettingsRepository settings,
	Outbox outbox,
	RateLimiter limiter,
	TimeProvider clock)
{
	/// <summary>The most ids a stats or presence request is answered for; the rest are ignored.</summary>
	internal const int MaxRequestedIds = 256;

	/// <summary>The most file names a beatmap info request is answered for; the rest are ignored.</summary>
	internal const int MaxRequestedFilenames = 100;

	/// <summary>Handles one packet from a connection.</summary>
	/// <param name="actor">The connection that sent it.</param>
	/// <param name="packet">The packet.</param>
	/// <param name="cancellationToken">A token that cancels the handling.</param>
	public Task HandleAsync(Connection actor, ClientPacket packet, CancellationToken cancellationToken)
	{
		var reader = PacketReader.ForPayload(packet.Payload);
		return packet.Type switch
		{
			ClientPacketType.Logout => Run(() => registry.Close(actor, SessionEndReason.LoggedOut)),
			ClientPacketType.UserStatsRequest => StatsRequestAsync(actor, reader.ReadInt32List().Take(MaxRequestedIds),
				cancellationToken),
			ClientPacketType.UserPresenceRequest => PresenceRequestAsync(actor,
				reader.ReadInt32List().Take(MaxRequestedIds), cancellationToken),
			ClientPacketType.UserPresenceRequestAll => PresenceRequestAsync(actor,
				registry.Online.Keys.Select(user => user.Id).ToList(), cancellationToken),
			ClientPacketType.TournamentMatchInfoRequest => TourneyInfoAsync(actor, reader.ReadInt32(),
				cancellationToken),
			ClientPacketType.TournamentJoinMatchChannel => TourneyJoinAsync(actor, reader.ReadInt32(),
				cancellationToken),
			ClientPacketType.TournamentLeaveMatchChannel =>
				TourneyLeaveAsync(actor, reader.ReadInt32(), cancellationToken),
			_ when actor is StableConnection osu => HandleOsuAsync(osu, packet.Type, packet.Payload, reader,
				cancellationToken),
			_ => Task.CompletedTask
		};
	}

	private Task HandleOsuAsync(StableConnection osu, ClientPacketType type, byte[] payload, PacketReader reader,
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
			ClientPacketType.BeatmapInfoRequest => BeatmapInfoAsync(osu, reader.ReadBeatmapInfoRequest(),
				cancellationToken),
			ClientPacketType.ChannelJoin => Run(() => JoinChannel(osu, reader.ReadOsuString())),
			ClientPacketType.ChannelPart => Run(() => PartChannel(osu, reader.ReadOsuString())),
			ClientPacketType.SendPublicMessage => PublicMessageAsync(osu, reader.ReadMessage(), cancellationToken),
			ClientPacketType.SendPrivateMessage => PrivateMessageAsync(osu, reader.ReadMessage(), cancellationToken),
			ClientPacketType.JoinLobby => Run(() => lobby.ScopeTo(osu).Watch()),
			ClientPacketType.PartLobby => Run(() => lobby.ScopeTo(osu).Unwatch()),
			ClientPacketType.StartSpectating => StartSpectatingAsync(osu, reader.ReadInt32(), cancellationToken),
			ClientPacketType.StopSpectating => Run(() => registry.FindSpectating(osu)?.ScopeTo(osu).StopSpectating()),
			ClientPacketType.CantSpectate => Run(() =>
				registry.FindSpectating(osu)?.ScopeTo(osu).ReportSpectatingFailed()),
			ClientPacketType.SpectateFrames => Run(() => ForwardFrames(osu, payload)),
			_ => HandleRoomAsync(osu, type, reader, cancellationToken)
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

	private async Task ChangeActionAsync(StableConnection osu, PacketReader reader)
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

		if (WireMap.Known<UserActivity>(action.Action) is not { } activity ||
		    WireMap.Known<GameMode>(action.Mode) is not { } mode) return;
		var status = new PlayerStatus(activity, action.InfoText, hash,
			action.MapId == 0 ? null : action.MapId, WireMap.Mods(action.Mods), mode);
		osu.ScopeTo(osu).SetStatus(status);
		await Task.CompletedTask;
	}

	private async Task StatsRequestAsync(Connection actor, IEnumerable<int> ids, CancellationToken cancellationToken)
	{
		var wanted = OnlineUsers(ids);

		var stats = new List<UserStats>();
		foreach (var user in wanted)
			stats.Add(await statsRepository.GetAsync(user.Record, user.Stable?.Status.GameMode ?? GameMode.Standard,
				cancellationToken));
		outbox.Send(actor, writer =>
		{
			for (var index = 0; index < wanted.Count; index++)
				StableView.WriteStats(writer, wanted[index], stats[index]);
		});
	}

	private async Task PresenceRequestAsync(Connection actor, IEnumerable<int> ids, CancellationToken cancellationToken)
	{
		var wanted = OnlineUsers(ids);

		var now = clock.GetUtcNow();
		var stats = new List<UserStats>();
		foreach (var user in wanted)
			stats.Add(await statsRepository.GetAsync(user.Record, user.Stable?.Status.GameMode ?? GameMode.Standard,
				cancellationToken));
		outbox.Send(actor, writer =>
		{
			for (var index = 0; index < wanted.Count; index++)
				StableView.WritePresence(writer, wanted[index], stats[index], now);
		});
	}

	// shortcut: one pass over the online users per request; a lookup by id needs an index in the registry.
	private List<User> OnlineUsers(IEnumerable<int> ids)
	{
		var asked = ids.ToList();
		var wanted = asked.ToHashSet();
		var online = registry.Online.Where(pair => wanted.Contains(pair.Key.Id))
			.ToDictionary(pair => pair.Key.Id, pair => pair.Value);
		return [.. asked.Where(online.ContainsKey).Select(id => online[id])];
	}

	private async Task FriendAsync(StableConnection osu, int id, bool add, CancellationToken cancellationToken)
	{
		if (await FindUserAsync(id, cancellationToken) is not { } target) return;
		var scoped = registry.Of(target).ScopeTo(osu).Relationships;
		if (add) await scoped.AddFriendAsync(cancellationToken);
		else await scoped.RemoveFriendAsync(cancellationToken);
	}

	private static void SetAway(StableConnection osu, string text)
	{
		osu.User.ScopeTo(osu).Profile.SetAway(string.IsNullOrEmpty(text) ? null : text);
	}

	private async Task BeatmapInfoAsync(StableConnection osu, BeatmapInfoRequest request,
		CancellationToken cancellationToken)
	{
		var entries = new List<BeatmapInfoResponse>();
		for (var index = 0; index < Math.Min(request.Filenames.Count, MaxRequestedFilenames); index++)
		{
			var page = await beatmaps.ListAsync(new BeatmapQuery(
				FileName: request.Filenames[index]), new PageRequest(0, 1), cancellationToken);
			if (page.Items.FirstOrDefault() is { } map)
				entries.Add(new BeatmapInfoResponse((short)index, map.Id,
					map.Beatmapset.Id, 0, (byte)map.Beatmapset.Status, -1, -1, -1, -1,
					map.Hash.ToString()));
		}

		outbox.Send(osu, writer => writer.BeatmapInfoReply(entries));
	}
}