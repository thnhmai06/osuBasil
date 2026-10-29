using Basil.Application.Multiplayer.Runtime.Rooms;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;

namespace Basil.Host.Shared.Views;

/// <summary>A slot of a room.</summary>
public sealed record SlotView(int Index, bool Locked, RoomSlotStatus? Status, GameTeam? Team, string? Mods,
	UserRef? Player, bool IsHost);

/// <summary>A room as the API shows it; the password is never shown.</summary>
public sealed record RoomView(int Id, int MatchId, string Name, bool IsTournament, bool IsPrivate, bool HasPassword, bool Locked,
	bool InProgress, UserRef Creator, UserRef? Host, IReadOnlyList<UserRef> Referees, IReadOnlyList<UserRef> Bans,
	int? BeatmapId, GameMode Mode, string Mods, bool Freemods, GameTeamType TeamType, GameWinCondition WinCondition,
	IReadOnlyList<SlotView> Slots)
{
	/// <summary>Describes a room.</summary>
	public static RoomView Of(Room room)
	{
		var host = room.Authority.Host;
		var settings = room.Settings.MatchSettings;
		return new RoomView(room.Id, room.Match.Id, room.Settings.Name, room.IsTournament, room.Settings.IsPrivate,
			room.Settings.Password.Length > 0, room.Slots.Locked, room.Rounds.InProgress, UserRef.Of(room.Authority.Creator),
			host is null ? null : UserRef.Of(host.Record), [.. room.Authority.Referees.Select(UserRef.Of)],
			[.. room.Members.Banned.Select(UserRef.Of)], room.Settings.Beatmap?.Id, settings.Mode, settings.Mods.ToString(),
			settings.Freemods, settings.TeamType, settings.WinCondition,
			[.. room.Slots.Select(slot => new SlotView(slot.Index, slot.Locked, slot.Status, slot.Team,
				slot.Mods?.ToString(), slot.Player is null ? null : UserRef.Of(slot.Player.Record),
				host is not null && slot.Player == host))]);
	}
}

/// <summary>A match as the API shows it.</summary>
public sealed record MatchView(int Id, string Name, bool IsPrivate, DateTimeOffset StartedAt, DateTimeOffset? EndedAt,
	UserRef Creator)
{
	/// <summary>Describes a match.</summary>
	public static MatchView Of(MatchRecord match)
	{
		return new MatchView(match.Id, match.Value.Name, match.Value.IsPrivate, match.Value.StartedAt,
			match.Value.EndedAt, UserRef.Of(match.Value.Creator));
	}
}
