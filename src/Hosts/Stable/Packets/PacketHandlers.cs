using Basil.Application.Beatmaps.Messages;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Scores.Messages;
using Basil.Application.Shared;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Shared;
using Basil.Domain.Users;
using Basil.Gateway;
using Basil.Gateway.Admission;
using Basil.Gateway.RateLimiting;
using Basil.Host.Stable.Support;
using Basil.Protocol.Stable.Models.Beatmaps;
using Basil.Protocol.Stable.Wire.Packets;
using Mediator;

namespace Basil.Host.Stable.Packets;

/// <summary>Handles the packets an osu! client sends; each handler works through the scopes of the sender's connection.</summary>
[AdmissionPolicy]
internal sealed partial class PacketHandlers(
	UserRegistry registry,
	Lobby lobby,
	ISender sender,
	Outbox outbox,
	RateLimiter limiter,
	TimeProvider clock)
{
	/// <summary>The most ids a stats or presence request is answered for; the rest are ignored.</summary>
	internal const int MaxRequestedIds = 256;

	/// <summary>The most file names a beatmap info request is answered for; the rest are ignored.</summary>
	internal const int MaxRequestedFilenames = 100;

	/// <summary>Every packet type a client can send, with the admission policy metadata it is received under.</summary>
	internal static IReadOnlyList<(string Name, IReadOnlyList<object> Metadata)> EntryPoints { get; } =
	[
		.. Enum.GetValues<ClientPacketType>().Select(type => (type.ToString(),
			(IReadOnlyList<object>)typeof(PacketHandlers).GetCustomAttributes(true)))
	];

	/// <summary>Handles one packet from a connection.</summary>
	/// <param name="actor">The connection that sent it.</param>
	/// <param name="packet">The packet.</param>
	/// <param name="ct">A token that cancels the handling.</param>
	public Task HandleAsync(Connection actor, ClientPacket packet, CancellationToken ct)
	{
		var reader = PacketReader.ForPayload(packet.Payload);
		return packet.Type switch
		{
			ClientPacketType.Logout => Run(() => registry.Close(actor, SessionEndReason.LoggedOut)),
			ClientPacketType.UserStatsRequest => StatsRequestAsync(actor, reader.ReadInt32List().Take(MaxRequestedIds),
				ct),
			ClientPacketType.UserPresenceRequest => PresenceRequestAsync(actor,
				reader.ReadInt32List().Take(MaxRequestedIds), ct),
			ClientPacketType.UserPresenceRequestAll => PresenceRequestAsync(actor,
				registry.Online.Keys.Select(user => user.Id).ToList(), ct),
			ClientPacketType.TournamentMatchInfoRequest => TourneyInfoAsync(actor, reader.ReadInt32(),
				ct),
			ClientPacketType.TournamentJoinMatchChannel => TourneyJoinAsync(actor, reader.ReadInt32(),
				ct),
			ClientPacketType.TournamentLeaveMatchChannel =>
				TourneyLeaveAsync(actor, reader.ReadInt32(), ct),
			_ when actor is StableConnection osu => HandleOsuAsync(osu, packet.Type, packet.Payload, reader,
				ct),
			_ => Task.CompletedTask
		};
	}

	private Task HandleOsuAsync(StableConnection osu, ClientPacketType type, byte[] payload, PacketReader reader,
		CancellationToken ct)
	{
		return type switch
		{
			ClientPacketType.ChangeAction => ChangeActionAsync(osu, reader, ct),
			ClientPacketType.RequestStatusUpdate => StatsRequestAsync(osu, [osu.Record.Id], ct),
			ClientPacketType.FriendAdd => FriendAsync(osu, reader.ReadInt32(), true, ct),
			ClientPacketType.FriendRemove => FriendAsync(osu, reader.ReadInt32(), false, ct),
			ClientPacketType.SetAwayMessage => SetAwayAsync(osu, reader.ReadMessage().Text, ct),
			ClientPacketType.ToggleBlockNonFriendDms => sender
				.Send(new SetPmPrivate(osu, reader.ReadInt32() == 1), ct).AsTask(),
			ClientPacketType.BeatmapInfoRequest => BeatmapInfoAsync(osu, reader.ReadBeatmapInfoRequest(),
				ct),
			ClientPacketType.ChannelJoin => JoinChannelAsync(osu, reader.ReadOsuString(), ct),
			ClientPacketType.ChannelPart => PartChannelAsync(osu, reader.ReadOsuString(), ct),
			ClientPacketType.SendPublicMessage => PublicMessageAsync(osu, reader.ReadMessage(), ct),
			ClientPacketType.SendPrivateMessage => PrivateMessageAsync(osu, reader.ReadMessage(), ct),
			ClientPacketType.JoinLobby => sender.Send(new WatchLobby(osu), ct).AsTask(),
			ClientPacketType.PartLobby => sender.Send(new UnwatchLobby(osu), ct).AsTask(),
			ClientPacketType.StartSpectating => StartSpectatingAsync(osu, reader.ReadInt32(), ct),
			ClientPacketType.StopSpectating => StopSpectatingAsync(osu, ct),
			ClientPacketType.CantSpectate => ReportSpectatingFailedAsync(osu, ct),
			ClientPacketType.SpectateFrames => ForwardFramesAsync(osu, payload, ct),
			_ => HandleRoomAsync(osu, type, reader, ct)
		};
	}

	private static Task Run(Action action)
	{
		action();
		return Task.CompletedTask;
	}

	private async Task ChangeActionAsync(StableConnection osu, PacketReader reader,
		CancellationToken ct)
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
		await sender.Send(new SetPlayerStatus(osu, status), ct);
	}

	private async Task StatsRequestAsync(Connection actor, IEnumerable<int> ids, CancellationToken ct)
	{
		var wanted = OnlineUsers(ids);

		var stats = new List<UserStats>();
		foreach (var user in wanted)
			stats.Add(await sender.Send(new GetUserStats(user, user.Stable?.Status.GameMode ?? GameMode.Standard),
				ct));
		outbox.Send(actor, writer =>
		{
			for (var index = 0; index < wanted.Count; index++)
				StableView.WriteStats(writer, wanted[index], stats[index]);
		});
	}

	private async Task PresenceRequestAsync(Connection actor, IEnumerable<int> ids, CancellationToken ct)
	{
		var wanted = OnlineUsers(ids);

		var now = clock.GetUtcNow();
		var stats = new List<UserStats>();
		foreach (var user in wanted)
			stats.Add(await sender.Send(new GetUserStats(user, user.Stable?.Status.GameMode ?? GameMode.Standard),
				ct));
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

	private async Task FriendAsync(StableConnection osu, int id, bool add, CancellationToken ct)
	{
		var target = UserReference.ById(id);
		if (add) await sender.Send(new AddFriend(osu, target), ct);
		else await sender.Send(new RemoveFriend(osu, target), ct);
	}

	private async Task SetAwayAsync(StableConnection osu, string text, CancellationToken ct)
	{
		await sender.Send(new SetAwayMessage(osu, string.IsNullOrEmpty(text) ? null : text), ct);
	}

	private async Task BeatmapInfoAsync(StableConnection osu, BeatmapInfoRequest request,
		CancellationToken ct)
	{
		var entries = new List<BeatmapInfoResponse>();
		for (var index = 0; index < Math.Min(request.Filenames.Count, MaxRequestedFilenames); index++)
		{
			if (await sender.Send(new FindBeatmapByFileName(request.Filenames[index]), ct) is { } map)
				entries.Add(new BeatmapInfoResponse((short)index, map.Id,
					map.Beatmapset.Id, 0, (byte)map.Beatmapset.Status, -1, -1, -1, -1,
					map.Hash.ToString()));
		}

		outbox.Send(osu, writer => writer.BeatmapInfoReply(entries));
	}
}