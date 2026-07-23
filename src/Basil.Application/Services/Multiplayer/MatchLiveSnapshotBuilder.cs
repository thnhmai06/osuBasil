using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Multiplayer;

namespace Basil.Application.Services.Multiplayer;

/// <summary>
///     Builds the lightweight, in-memory-only payloads pushed over the live SSE channels
///     (<see cref="IMatchLiveEvents" />) — deliberately cheaper than <see cref="MatchReportService" />'s
///     DB-backed <c>MatchReport</c>, since these fire on every state-changing packet (join, ready,
///     slot change, ...), not on demand. Consequently, the main channel carries live slot/map/state
///     only, not aggregated team scores or a round winner — those come from polling GET /match/{id}.
/// </summary>
public static class MatchLiveSnapshotBuilder
{
    public static MatchLiveSnapshot BuildMain(MatchSession match, IPlayerSessionRegistry sessionRegistry)
    {
        var host = sessionRegistry.GetById(match.HostId);

        var referees = match.Referees
            .Select(id =>
            {
                var s = sessionRegistry.GetById(id);
                return new UserBrief(id, s?.Name);
            })
            .ToArray();

        var slots = match.Slots
            .Select((slot, index) =>
            {
                var s = slot.PlayerId is { } pid ? sessionRegistry.GetById(pid) : null;
                return new MatchLiveSlot(
                    index, slot.PlayerId, s?.Name, s?.Geoloc.Country.ToAcronym(),
                    slot.Status.ToString(), slot.Team.ToString(), (int)slot.Mods);
            })
            .ToArray();

        return new MatchLiveSnapshot(
            match.DbId, match.Name, match.MapId, match.MapMd5, match.InProgress,
            match.WinCondition, match.TeamType, match.Mode, (int)match.Mods, match.Freemods,
            host is not null ? new UserBrief(host.Id, host.Name, host.Geoloc.Country.ToAcronym()) : null,
            referees, slots);
    }

    public static PlayerLiveScore BuildPlayerScore(string playerName, ScoreFrameData frame)
    {
        return new PlayerLiveScore(
            playerName, frame.Time, frame.Num300, frame.Num100, frame.Num50, frame.NumGeki, frame.NumKatu,
            frame.NumMiss, frame.TotalScore, frame.MaxCombo, frame.CurrentCombo, frame.Perfect, frame.CurrentHp);
    }

    /// <summary>
    ///     The `api.` host's `/match/{id}/settings` payload shape — never the raw <see cref="MatchSession.Password" />,
    ///     only whether one is set, even for an admin-elevated caller (a public, unauthenticated SSE
    ///     channel is not the place to leak it).
    /// </summary>
    /// <summary>The `api.` host's `/match/{id}/live` payload — room-wide "currently playing" info, no per-player data.</summary>
    public static MatchLiveStatus BuildLiveStatus(MatchSession match)
    {
        return new MatchLiveStatus(match.InProgress, match.CurrentRoundId, match.MapId, match.Mode);
    }

    public static MatchSettingsView BuildSettings(MatchSession match)
    {
        var size = match.Slots.Count(s => s.Status != SlotStatus.Locked);
        return new MatchSettingsView(
            match.DbId, match.Name, !string.IsNullOrEmpty(match.Password), match.IsPrivate, match.IsLocked, size,
            match.MapId, match.MapName, (int)match.Mods, match.Freemods,
            match.TeamType, match.WinCondition,
            match.HostId == 0 ? null : match.HostId, match.Referees.ToArray());
    }

    public static MatchHostView BuildHost(MatchSession match, IPlayerSessionRegistry sessionRegistry)
    {
        if (match.HostId == 0) return new MatchHostView(null, null);

        var host = sessionRegistry.GetById(match.HostId);
        return new MatchHostView(match.HostId, host?.Name);
    }

    public static MatchRefereesView BuildRefs(MatchSession match, IPlayerSessionRegistry sessionRegistry)
    {
        var referees = match.Referees
            .Select(id => new UserBrief(id, sessionRegistry.GetById(id)?.Name))
            .ToArray();
        return new MatchRefereesView(referees);
    }

    public static MatchBansView BuildBans(MatchSession match, IPlayerSessionRegistry sessionRegistry)
    {
        var banned = match.BannedIds
            .Select(id => new UserBrief(id, sessionRegistry.GetById(id)?.Name))
            .ToArray();
        return new MatchBansView(banned);
    }

    public static MatchTimerView BuildTimer(MatchSession match)
    {
        if (match.PendingTimer is null || match.TimerStartedAt is null || match.TimerTotalSeconds is null)
            return new MatchTimerView(false, null, false);

        var elapsed = (DateTimeOffset.UtcNow - match.TimerStartedAt.Value).TotalSeconds;
        var remaining = Math.Max(0, match.TimerTotalSeconds.Value - (int)elapsed);
        return new MatchTimerView(true, remaining, match.PendingTimerIsAutoStart);
    }

    public static MatchSlotsView BuildSlots(MatchSession match, IPlayerSessionRegistry sessionRegistry)
    {
        var slots = new Dictionary<int, SlotView>();
        for (var i = 0; i < match.Slots.Count; i++)
        {
            var slot = match.Slots[i];
            var user = slot.PlayerId is { } pid ? sessionRegistry.GetById(pid) : null;
            slots[i] = new SlotView(slot.PlayerId, user?.Name,
                slot.PlayerId is not null ? slot.Team.ToString() : null,
                slot.Status == SlotStatus.Locked);
        }

        return new MatchSlotsView(slots);
    }
}

/// <summary>Payload for the SSE `/match/{id}/live` channel — idle (no events) outside of an active round.</summary>
public sealed record MatchLiveStatus(bool InProgress, int? CurrentRoundId, int MapId, GameMode Mode);

/// <summary>Payload for the SSE `/match/{id}/settings` channel and the response of every settings write.</summary>
public sealed record MatchSettingsView(
    int Id,
    string Name,
    bool HasPassword,
    bool IsPrivate,
    bool IsLocked,
    int Size,
    int MapId,
    string MapName,
    int Mods,
    bool Freemod,
    MatchTeamType TeamType,
    MatchWinCondition WinCondition,
    int? HostId,
    IReadOnlyCollection<int> RefereeIds);

/// <summary>Brief user info for embedded references (host, referees, slots).</summary>
public sealed record UserBrief(int? UserId, string? UserName, string? Country = null);

/// <summary>Payload for the SSE /match/{id} main channel.</summary>
public sealed record MatchLiveSnapshot(
    int MatchId,
    string Name,
    int CurrentMapId,
    string CurrentMapMd5,
    bool InProgress,
    MatchWinCondition WinCondition,
    MatchTeamType TeamType,
    GameMode Mode,
    int Mods,
    bool Freemods,
    UserBrief? Host,
    UserBrief[] Referees,
    MatchLiveSlot[] Slots);

public sealed record MatchLiveSlot(
    int SlotIndex,
    int? UserId,
    string? UserName,
    string? Country,
    string Status,
    string Team,
    int Mods);

/// <summary>Payload for the SSE /match/{id}/{playerName} channel.</summary>
public sealed record PlayerLiveScore(
    string PlayerName,
    int Time,
    int Num300,
    int Num100,
    int Num50,
    int NumGeki,
    int NumKatu,
    int NumMiss,
    int TotalScore,
    int MaxCombo,
    int CurrentCombo,
    bool Perfect,
    int CurrentHp);

/// <summary>Payload for the SSE /spec/{id} channel.</summary>
public sealed record PlayerInputFrame(string PlayerName, string DataBase64);

/// <summary>Payload for `GET /matches/{matchId}/hosts` — null fields when the room has no host (id 0).</summary>
public sealed record MatchHostView(int? HostId, string? HostName);

/// <summary>Payload for `GET /matches/{matchId}/refs`.</summary>
public sealed record MatchRefereesView(IReadOnlyList<UserBrief> Referees);

/// <summary>Payload for `GET /matches/{matchId}/ban`.</summary>
public sealed record MatchBansView(IReadOnlyList<UserBrief> BannedUsers);

/// <summary>Payload for `GET /matches/{matchId}/timer`. <c>AutoStart</c> mirrors <see cref="MatchSession.PendingTimerIsAutoStart" />.</summary>
public sealed record MatchTimerView(bool Running, int? SecondsRemaining, bool AutoStart);

/// <summary>
///     One slot in `GET /matches/{matchId}/slots`'s dict response — <see cref="Locked" /> is always
///     `false` for an occupied slot (an occupied slot's underlying <see cref="SlotStatus" /> can never
///     also be <see cref="SlotStatus.Locked" />).
/// </summary>
public sealed record SlotView(int? UserId, string? UserName, string? Team, bool Locked);

/// <summary>
///     Payload for `GET/PUT/PATCH /matches/{matchId}/slots` — every slot 0-15 always present as a
///     dict key (JSON-serializes as string keys), per the owner's RFC 7396-friendly dict-over-array
///     design for every multiplayer slots representation.
/// </summary>
public sealed record MatchSlotsView(IReadOnlyDictionary<int, SlotView> Slots);
