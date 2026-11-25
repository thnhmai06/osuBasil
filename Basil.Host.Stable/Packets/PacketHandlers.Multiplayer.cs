using Basil.Application.Beatmaps.Messages;
using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Beatmaps;
using Basil.Domain.Game;
using Basil.Domain.Multiplayer.Report.Matches;
using Basil.Domain.Multiplayer.Room;
using Basil.Host.Stable.Support;
using Basil.Protocol.Game.Stable.Models.Multiplayer;
using Basil.Protocol.Game.Stable.Wire.Packets;
using Basil.Shared.Structures.Cryptography;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Host.Stable.Packets;

internal sealed partial class PacketHandlers
{
	private Task HandleRoomAsync(StableSession osu, ClientPacketType type, PacketReader reader,
		CancellationToken ct)
	{
		switch (type)
		{
			case ClientPacketType.CreateMatch:
				return CreateMatchAsync(osu, reader.ReadMatch(), ct);
			case ClientPacketType.JoinMatch:
				return JoinMatchAsync(osu, reader.ReadJoinMatch(), ct);
		}

		if (lobby.RoomOf(osu) is not { } room) return Task.CompletedTask;
		return type switch
		{
			ClientPacketType.PartMatch => Ask(new LeaveRoom(osu, room), ct),
			ClientPacketType.MatchChangeSlot => Ask(new ChangeSlot(osu, room, WireMap.SlotFromWire(reader.ReadInt32())), ct),
			ClientPacketType.MatchReady => Ask(new SetReady(osu, room, true), ct),
			ClientPacketType.MatchNotReady => Ask(new SetReady(osu, room, false), ct),
			ClientPacketType.MatchLock => Ask(new ToggleSlotLock(osu, room, WireMap.SlotFromWire(reader.ReadInt32())), ct),
			ClientPacketType.MatchChangeMods => Ask(new SetMods(osu, room, WireMap.Mods(reader.ReadInt32())),
				ct),
			ClientPacketType.MatchChangeTeam => Ask(new ToggleTeam(osu, room), ct),
			ClientPacketType.MatchNoBeatmap => Ask(new SetHasMap(osu, room, false), ct),
			ClientPacketType.MatchHasBeatmap => Ask(new SetHasMap(osu, room, true), ct),
			ClientPacketType.MatchStart => Ask(new StartRound(osu, room), ct),
			ClientPacketType.MatchLoadComplete => Ask(new MarkRoundLoaded(osu, room), ct),
			ClientPacketType.MatchSkipRequest => Ask(new SkipRound(osu, room), ct),
			ClientPacketType.MatchFailed => Ask(new FailRound(osu, room), ct),
			ClientPacketType.MatchComplete => Ask(new CompleteRound(osu, room), ct),
			ClientPacketType.MatchTransferHost =>
				TransferHostAsync(osu, room, reader.ReadInt32(), ct),
			ClientPacketType.MatchInvite => InviteAsync(osu, room, reader.ReadInt32(), ct),
			ClientPacketType.MatchChangePassword => Ask(
				new ConfigureRoom(osu, room, new RoomSettingsChange(Password: reader.ReadMatch().Password)),
				ct),
			ClientPacketType.MatchChangeSettings => ChangeSettingsAsync(osu, room, reader.ReadMatch(),
				ct),
			ClientPacketType.MatchScoreUpdate => sender.Send(new RelayScore(osu, room, reader.ReadScoreFrame()), ct).AsTask(),
			_ => Task.CompletedTask
		};
	}

	private async Task CreateMatchAsync(StableSession osu, RoomStatePacket match,
		CancellationToken ct)
	{
		var (room, result) = await sender.Send(
			new OpenRoom(osu, match.Name, match.Password, RoomSource.Game, false, SettingsOf(match),
				await BeatmapOf(match)), ct);
		if (room is null || result != LobbyResult.Ok) outbox.Send(osu, writer => writer.MatchJoinFail());
	}

	private async Task JoinMatchAsync(StableSession osu, JoinMatchRequest request,
		CancellationToken ct)
	{
		var result = lobby.FindByWireId(request.MatchId) is { IsClosed: false } room
			? await sender.Send(new JoinRoom(osu, room, request.Password), ct)
			: default;
		if (result != RoomJoinResult.Ok) outbox.Send(osu, writer => writer.MatchJoinFail());
	}

	private static MatchSettings SettingsOf(RoomStatePacket match)
	{
		var settings = new MatchSettings();
		if (ModeOf(match) is { } mode) settings.Mode = mode;
		if (WireMap.Known<GameTeamType>(match.TeamType) is { } teamType) settings.TeamType = teamType;
		if (WireMap.Known<GameWinCondition>(match.WinCondition) is { } winCondition)
			settings.WinCondition = winCondition;
		return settings;
	}

	private static GameMode? ModeOf(RoomStatePacket match)
	{
		return WireMap.Known<GameMode>(match.Mode);
	}

	private async Task<BeatmapReference?> BeatmapOf(RoomStatePacket match)
	{
		if (match.MapMd5.Length != 32 || ModeOf(match) is null) return null;

		var hash = new Md5Hash(match.MapMd5);
		var known = await sender.Send(new FindBeatmapByHash(hash));
		return new BeatmapReference(hash, match.MapId, match.MapName, known);
	}

	private async Task ChangeSettingsAsync(StableSession osu, Room room, RoomStatePacket match,
		CancellationToken ct)
	{
		await sender.Send(new ChangeRoomSettings(osu, room,
			new WantedRoomSettings(match.Name, WireMap.Mods(match.Mods), match.FreeMods, await BeatmapOf(match),
				ModeOf(match), WireMap.Known<GameTeamType>(match.TeamType),
				WireMap.Known<GameWinCondition>(match.WinCondition))), ct);
	}

	private Task Ask<TMessage>(TMessage message, CancellationToken ct)
		where TMessage : IRequest<RoomActionResult>
	{
		return sender.Send(message, ct).AsTask();
	}

	private Task TransferHostAsync(StableSession osu, Room room, int slot, CancellationToken ct)
	{
		return room.Slots.FirstOrDefault(candidate => candidate.Index == WireMap.SlotFromWire(slot)) is { } target && room.PlayerAt(target) is { } player
			? Ask(new SetRoomHost(osu, room, player.Record.Id), ct)
			: Task.CompletedTask;
	}

	private async Task InviteAsync(StableSession osu, Room room, int userId, CancellationToken ct)
	{
		await sender.Send(new InvitePlayer(osu, room, userId), ct);
	}

	private async Task TourneyInfoAsync(Session actor, int matchId, CancellationToken ct)
	{
		if (lobby.FindByWireId(matchId) is not { IsClosed: false } room) return;
		if (await sender.Send(new InspectRoom(actor, room), ct) is not RoomJoinResult.Ok) return;
		var match = StableView.Match(room);
		outbox.Send(actor, writer => writer.UpdateMatch(match, false));
	}

	private async Task TourneyJoinAsync(Session actor, int matchId, CancellationToken ct)
	{
		if (lobby.FindByWireId(matchId) is not { IsClosed: false } room) return;
		if (!await sender.Send(new CanViewMatch(actor, room.Match.Id), ct)) return;
		await sender.Send(new ObserveRoom(actor, room), ct);
	}

	private async Task TourneyLeaveAsync(Session actor, int matchId, CancellationToken ct)
	{
		if (lobby.FindByWireId(matchId) is { } room)
			await sender.Send(new StopObservingRoom(actor, room), ct);
	}
}