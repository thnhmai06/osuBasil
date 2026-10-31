using Basil.Application.Multiplayer.Capabilities.Rooms;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Host.Bancho.Support;
using Basil.Protocol.Bancho.Models.Multiplayer;
using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Host.Bancho.Packets;

internal sealed partial class PacketHandlers
{
	private Task HandleRoomAsync(BanchoConnection osu, ClientPacketType type, byte[] payload, PacketReader reader,
		CancellationToken cancellationToken)
	{
		switch (type)
		{
			case ClientPacketType.CreateMatch:
				return CreateMatchAsync(osu, reader.ReadMatch(), cancellationToken);
			case ClientPacketType.JoinMatch:
				return JoinMatchAsync(osu, reader.ReadJoinMatch(), cancellationToken);
		}

		if (lobby.RoomOf(osu) is not { } room) return Task.CompletedTask;
		var scoped = room.ScopeTo(osu);
		return type switch
		{
			ClientPacketType.PartMatch => scoped.Members.LeaveAsync(cancellationToken),
			ClientPacketType.MatchChangeSlot => scoped.Slots.ChangeAsync(reader.ReadInt32(), cancellationToken),
			ClientPacketType.MatchReady => scoped.Slots.SetReadyAsync(true, cancellationToken),
			ClientPacketType.MatchNotReady => scoped.Slots.SetReadyAsync(false, cancellationToken),
			ClientPacketType.MatchLock => scoped.Slots.ToggleLockAsync(reader.ReadInt32(), cancellationToken),
			ClientPacketType.MatchChangeMods => scoped.Slots.SetModsAsync((GameMods)reader.ReadInt32(),
				cancellationToken),
			ClientPacketType.MatchChangeTeam => scoped.Slots.ToggleTeamAsync(cancellationToken),
			ClientPacketType.MatchNoBeatmap => scoped.Slots.SetHasMapAsync(false, cancellationToken),
			ClientPacketType.MatchHasBeatmap => scoped.Slots.SetHasMapAsync(true, cancellationToken),
			ClientPacketType.MatchStart => scoped.Rounds.StartAsync(cancellationToken),
			ClientPacketType.MatchLoadComplete => scoped.Rounds.MarkLoadedAsync(cancellationToken),
			ClientPacketType.MatchSkipRequest => scoped.Rounds.SkipAsync(cancellationToken),
			ClientPacketType.MatchFailed => scoped.Rounds.FailAsync(cancellationToken),
			ClientPacketType.MatchComplete => scoped.Rounds.CompleteAsync(cancellationToken),
			ClientPacketType.MatchTransferHost =>
				TransferHostAsync(scoped, room, reader.ReadInt32(), cancellationToken),
			ClientPacketType.MatchInvite => InviteAsync(scoped, reader.ReadInt32(), cancellationToken),
			ClientPacketType.MatchChangePassword => ConfigureAsync(scoped,
				new RoomSettingsChange(Password: reader.ReadMatch().Password), cancellationToken),
			ClientPacketType.MatchChangeSettings => ChangeSettingsAsync(scoped, room, reader.ReadMatch(),
				cancellationToken),
			ClientPacketType.MatchScoreUpdate => Run(() => ForwardScore(osu, room, reader)),
			_ => Task.CompletedTask
		};
	}

	private async Task CreateMatchAsync(BanchoConnection osu, RoomStatePacket match,
		CancellationToken cancellationToken)
	{
		var matchSettings = SettingsOf(match);
		var (room, result) = await lobby.ScopeTo(osu).OpenRoomAsync(match.Name, match.Password, false, false,
			matchSettings,
			cancellationToken);
		if (room is null || !result.IsOk)
		{
			outbox.Send(osu, writer => writer.MatchJoinFail());
			return;
		}

		if (BeatmapOf(match) is { } beatmapChange)
			await room.ScopeTo(osu).Settings.ConfigureAsync(await beatmapChange, cancellationToken);
	}

	private async Task JoinMatchAsync(BanchoConnection osu, JoinMatchRequest request,
		CancellationToken cancellationToken)
	{
		var result = lobby.Find(request.MatchId) is { IsClosed: false } room
			? await room.ScopeTo(osu).Members.JoinAsync(request.Password, cancellationToken)
			: default;
		if (!result.IsOk) outbox.Send(osu, writer => writer.MatchJoinFail());
	}

	private static MatchSettings SettingsOf(RoomStatePacket match)
	{
		var settings = new MatchSettings();
		if (Enum.IsDefined((GameMode)match.Mode)) settings.Mode = (GameMode)match.Mode;
		if (Enum.IsDefined((GameTeamType)match.TeamType)) settings.TeamType = (GameTeamType)match.TeamType;
		if (Enum.IsDefined((GameWinCondition)match.WinCondition))
			settings.WinCondition = (GameWinCondition)match.WinCondition;
		return settings;
	}

	private Task<RoomSettingsChange>? BeatmapOf(RoomStatePacket match)
	{
		return match.MapMd5.Length == 32 && Enum.IsDefined((GameMode)match.Mode) ? BuildAsync() : null;

		async Task<RoomSettingsChange> BuildAsync()
		{
			var hash = new Md5(match.MapMd5);
			var known = await beatmaps.GetAsync(hash);
			return new RoomSettingsChange(
				Beatmap: new BeatmapReference(hash, match.MapId, match.MapName, (GameMode)match.Mode, known));
		}
	}

	private async Task ChangeSettingsAsync(ScopedRoom scoped, Room room, RoomStatePacket match,
		CancellationToken cancellationToken)
	{
		var current = room.Settings.MatchSettings;
		var change = new RoomSettingsChange(
			match.Name != room.Settings.Name ? match.Name : null,
			Mode: Enum.IsDefined((GameMode)match.Mode) && (GameMode)match.Mode != current.Mode
				? (GameMode)match.Mode
				: null,
			Mods: (GameMods)match.Mods != current.Mods ? (GameMods)match.Mods : null,
			Freemods: match.FreeMods != current.Freemods ? match.FreeMods : null,
			TeamType: Enum.IsDefined((GameTeamType)match.TeamType) && (GameTeamType)match.TeamType != current.TeamType
				? (GameTeamType)match.TeamType
				: null,
			WinCondition: Enum.IsDefined((GameWinCondition)match.WinCondition) &&
			              (GameWinCondition)match.WinCondition != current.WinCondition
				? (GameWinCondition)match.WinCondition
				: null);
		if (BeatmapOf(match) is { } beatmapChange && room.Settings.Beatmap?.Hash.ToString() != match.MapMd5)
			change = change with { Beatmap = (await beatmapChange).Beatmap };

		await ConfigureAsync(scoped, change, cancellationToken);
	}

	private static Task ConfigureAsync(ScopedRoom scoped, RoomSettingsChange change,
		CancellationToken cancellationToken)
	{
		return scoped.Settings.ConfigureAsync(change, cancellationToken);
	}

	private static Task TransferHostAsync(ScopedRoom scoped, Room room, int slot, CancellationToken cancellationToken)
	{
		return room.Slots.At(slot)?.Player is { } player
			? scoped.Authority.SetHostAsync(player, cancellationToken)
			: Task.CompletedTask;
	}

	private async Task InviteAsync(ScopedRoom scoped, int userId, CancellationToken cancellationToken)
	{
		if (await FindUserAsync(userId, cancellationToken) is { } record)
			await scoped.Members.InviteAsync(registry.Of(record), cancellationToken);
	}

	private void ForwardScore(BanchoConnection osu, Room room, PacketReader reader)
	{
		if (room.Slots.Find(osu) is not { } slot || !room.Rounds.InProgress) return;
		var frame = reader.ReadScoreFrame() with { Id = slot.Index };
		outbox.Send(room.Slots.Select(other => (Connection?)other.Player).OfType<Connection>()
			.Concat(room.Members.Observers), writer => writer.MatchScoreUpdate(frame));
	}

	private async Task TourneyInfoAsync(Connection by, int matchId, CancellationToken cancellationToken)
	{
		if (by is not TourneyConnection || lobby.Find(matchId) is not { IsClosed: false } room) return;
		if (!await lobby.ScopeTo(by).CanViewAsync(room.Match, cancellationToken)) return;
		var match = BanchoView.Match(room);
		outbox.Send(by, writer => writer.UpdateMatch(match, false));
	}

	private async Task TourneyJoinAsync(Connection by, int matchId, CancellationToken cancellationToken)
	{
		if (by is not TourneyConnection || lobby.Find(matchId) is not { IsClosed: false } room) return;
		if (!await lobby.ScopeTo(by).CanViewAsync(room.Match, cancellationToken)) return;
		await room.ScopeTo(by).Members.ObserverJoinAsync(cancellationToken);
	}

	private async Task TourneyLeaveAsync(Connection by, int matchId, CancellationToken cancellationToken)
	{
		if (by is TourneyConnection && lobby.Find(matchId) is { } room)
			await room.ScopeTo(by).Members.ObserverLeaveAsync(cancellationToken);
	}
}