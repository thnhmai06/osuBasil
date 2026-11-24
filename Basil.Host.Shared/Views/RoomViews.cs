using Basil.Domain.Game;
using Basil.Domain.Multiplayer.Report.Matches;
using Basil.Domain.Multiplayer.Room;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Host.Shared.Views;

public sealed record SlotView(
	int Index,
	bool Locked,
	RoomSlotStatus? Status,
	GameTeam? Team,
	string? Mods,
	UserRef? Player,
	bool IsHost);

public sealed record RoomView(
	int Id,
	string Name,
	bool IsPrivate,
	bool HasPassword,
	bool Locked,
	bool InProgress,
	UserRef Creator,
	UserRef? Host,
	IReadOnlyList<UserRef> Referees,
	IReadOnlyList<UserRef> Bans,
	int? BeatmapId,
	GameMode Mode,
	string Mods,
	bool Freemods,
	GameTeamType TeamType,
	GameWinCondition WinCondition,
	IReadOnlyList<SlotView> Slots)
{
	public static RoomView Of(Room room)
	{
		var host = room.State.Members.Host?.Record;
		var settings = room.Settings.MatchSettings;
		return new RoomView(room.Id, room.Settings.Name, room.Settings.IsPrivate,
			room.Settings.Password.Length > 0, room.Slots.Locked, room.Rounds.InProgress,
			UserRef.Of(room.Creator),
			host is null ? null : UserRef.Of(host), [.. room.Referees.Select(UserRef.Of)],
			[.. room.Members.Banned.Select(UserRef.Of)], room.Settings.Beatmap?.Id, settings.Mode,
			settings.Mods.ToString(),
			settings.Freemods, settings.TeamType, settings.WinCondition,
			[
				.. room.Slots.Select(slot => new SlotView(slot.Index, slot.Locked, slot.Status, slot.Team,
					slot.Mods?.ToString(), slot.Player is null ? null : UserRef.Of(slot.Player),
					host is not null && slot.Player.Equals(host)))
			]);
	}
}

public sealed record MatchView(
	int Id,
	string Name,
	bool IsPrivate,
	DateTimeOffset StartedAt,
	DateTimeOffset? EndedAt,
	UserRef Creator)
{
	public static MatchView Of(MatchRecord match)
	{
		return new MatchView(match.Id, match.Value.Name, match.Value.IsPrivate, match.Value.StartedAt,
			match.Value.EndedAt, UserRef.Of(match.Value.Creator));
	}
}