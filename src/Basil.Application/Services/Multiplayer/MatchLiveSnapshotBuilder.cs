using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Users;
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
///     Every user reference is resolved via <see cref="UserBriefResolver" /> (fast for online players,
///     falling back to the cached <see cref="IUserRepository" /> for offline ones), which is why every
///     builder that can reference a user is async.
/// </summary>
public static class MatchLiveSnapshotBuilder
{
    public static async Task<MatchLiveSnapshot> BuildMain(MatchSession match, IPlayerSessionRegistry sessionRegistry,
        IUserRepository users, IMapRepository maps, CancellationToken cancellationToken = default)
    {
        var host = match.HostId == 0
            ? null
            : await ResolveOrPlaceholder(match.HostId, sessionRegistry, users, cancellationToken);

        var referees = new List<UserBrief>();
        foreach (var id in match.Referees)
            referees.Add(await ResolveOrPlaceholder(id, sessionRegistry, users, cancellationToken));

        var slots = new Dictionary<int, MatchLiveSlot>();
        for (var index = 0; index < match.Slots.Count; index++)
        {
            var slot = match.Slots[index];
            var user = slot.PlayerId is { } pid
                ? await ResolveOrPlaceholder(pid, sessionRegistry, users, cancellationToken)
                : null;
            slots[index] = new MatchLiveSlot(user, slot.Status.ToString(), slot.Team.ToString(), (int)slot.Mods);
        }

        var beatmap = await ResolveBeatmapAsync(match.MapMd5, maps, cancellationToken);

        return new MatchLiveSnapshot(
            match.DbId, match.Name, match.MapId, match.MapMd5, match.InProgress,
            match.WinCondition, match.TeamType, match.Mode, (int)match.Mods, match.Freemods,
            host, referees, beatmap, slots);
    }

    public static PlayerLiveScore BuildPlayerScore(PlayerSession player, ScoreFrameData frame)
    {
        return new PlayerLiveScore(
            new UserBrief(player.Id, player.Name, player.Geoloc.Country.ToAcronym()),
            frame.Time, frame.Num300, frame.Num100, frame.Num50, frame.NumGeki, frame.NumKatu,
            frame.NumMiss, frame.TotalScore, frame.MaxCombo, frame.CurrentCombo, frame.Perfect, frame.CurrentHp);
    }

    /// <summary>The `api.` host's `/match/{id}/live` payload — room-wide "currently playing" info, no per-player data.</summary>
    public static MatchLiveStatus BuildLiveStatus(MatchSession match)
    {
        return new MatchLiveStatus(match.InProgress, match.CurrentRoundId, match.MapId, match.Mode);
    }

    /// <summary>
    ///     The `api.` host's `/match/{id}/settings` payload shape — never the raw <see cref="MatchSession.Password" />,
    ///     only whether one is set, even for an admin-elevated caller (a public, unauthenticated SSE
    ///     channel is not the place to leak it).
    /// </summary>
    public static async Task<MatchSettingsView> BuildSettings(MatchSession match, IPlayerSessionRegistry sessionRegistry,
        IUserRepository users, IMapRepository maps, CancellationToken cancellationToken = default)
    {
        var size = match.Slots.Count(s => s.Status != SlotStatus.Locked);

        var host = match.HostId == 0
            ? null
            : await ResolveOrPlaceholder(match.HostId, sessionRegistry, users, cancellationToken);

        var referees = new List<UserBrief>();
        foreach (var id in match.Referees)
            referees.Add(await ResolveOrPlaceholder(id, sessionRegistry, users, cancellationToken));

        var beatmap = await ResolveBeatmapAsync(match.MapMd5, maps, cancellationToken);

        return new MatchSettingsView(
            match.DbId, match.Name, !string.IsNullOrEmpty(match.Password), match.IsPrivate, match.IsLocked, size,
            match.MapId, match.MapName, (int)match.Mods, match.Freemods,
            match.TeamType, match.WinCondition,
            host, referees, beatmap);
    }

    public static async Task<MatchHostView> BuildHost(MatchSession match, IPlayerSessionRegistry sessionRegistry,
        IUserRepository users, CancellationToken cancellationToken = default)
    {
        if (match.HostId == 0) return new MatchHostView(null);

        var host = await ResolveOrPlaceholder(match.HostId, sessionRegistry, users, cancellationToken);
        return new MatchHostView(host);
    }

    public static async Task<MatchRefereesView> BuildRefs(MatchSession match, IPlayerSessionRegistry sessionRegistry,
        IUserRepository users, CancellationToken cancellationToken = default)
    {
        var referees = new List<UserBrief>();
        foreach (var id in match.Referees)
            referees.Add(await ResolveOrPlaceholder(id, sessionRegistry, users, cancellationToken));

        return new MatchRefereesView(referees);
    }

    public static async Task<MatchBansView> BuildBans(MatchSession match, IPlayerSessionRegistry sessionRegistry,
        IUserRepository users, CancellationToken cancellationToken = default)
    {
        var banned = new List<UserBrief>();
        foreach (var id in match.BannedIds)
            banned.Add(await ResolveOrPlaceholder(id, sessionRegistry, users, cancellationToken));

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

    public static async Task<MatchSlotsView> BuildSlots(MatchSession match, IPlayerSessionRegistry sessionRegistry,
        IUserRepository users, CancellationToken cancellationToken = default)
    {
        var slots = new Dictionary<int, SlotView>();
        for (var i = 0; i < match.Slots.Count; i++)
        {
            var slot = match.Slots[i];
            var user = slot.PlayerId is { } pid
                ? await ResolveOrPlaceholder(pid, sessionRegistry, users, cancellationToken)
                : null;
            slots[i] = new SlotView(user, slot.PlayerId is not null ? slot.Team.ToString() : null,
                slot.Status == SlotStatus.Locked);
        }

        return new MatchSlotsView(slots);
    }

    /// <summary>
    ///     Shared by every beatmap embed in this plan (live snapshot, settings view, TRT rounds/live
    ///     info) — a blank md5 means "no beatmap assigned yet" (a freshly created empty room), which is
    ///     never worth a repository round-trip and must not be confused with a stale/unresolvable md5
    ///     (both end up `null`, but only the latter is an actual lookup miss).
    /// </summary>
    public static Task<Beatmap?> ResolveBeatmapAsync(string mapMd5, IMapRepository maps,
        CancellationToken cancellationToken = default)
    {
        return string.IsNullOrEmpty(mapMd5)
            ? Task.FromResult<Beatmap?>(null)
            : maps.FetchOneAsync(md5: mapMd5, includePrivate: true, cancellationToken: cancellationToken);
    }

    /// <summary>
    ///     Every host/referee/ban/slot-occupant id embedded by this plan is, by definition, actually
    ///     assigned/referenced — unlike <see cref="UserBriefResolver.ResolveAsync" />'s plain null
    ///     (which the caller uses for genuine structural absence, e.g. host id 0 or an empty slot), a
    ///     resolution failure here must never silently shrink a list or get confused with "nothing is
    ///     assigned" — it falls back to a placeholder that still carries the real id.
    /// </summary>
    public static async Task<UserBrief> ResolveOrPlaceholder(int userId, IPlayerSessionRegistry sessionRegistry,
        IUserRepository users, CancellationToken cancellationToken)
    {
        return await UserBriefResolver.ResolveAsync(userId, sessionRegistry, users, cancellationToken)
               ?? new UserBrief(userId, "Unknown", Country.Xx.ToAcronym());
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
    UserBrief? Host,
    IReadOnlyList<UserBrief> Referees,
    Beatmap? Beatmap);

/// <summary>
///     The one reused `{id, name, country}` embed for every user reference across this plan's
///     routes/SSE payloads (settings host/referees, hosts/refs/bans views, slot occupants, TRT
///     actor/target/winner/scorer, per-player live streams) — resolved via <see cref="UserBriefResolver" />.
/// </summary>
public sealed record UserBrief(int Id, string Name, string Country);

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
    IReadOnlyList<UserBrief> Referees,
    Beatmap? Beatmap,
    IReadOnlyDictionary<int, MatchLiveSlot> Slots);

public sealed record MatchLiveSlot(
    UserBrief? User,
    string Status,
    string Team,
    int Mods);

/// <summary>Payload for the SSE /match/{id}/{playerName} channel.</summary>
public sealed record PlayerLiveScore(
    UserBrief User,
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
public sealed record PlayerInputFrame(UserBrief User, string DataBase64);

/// <summary>Payload for `GET /matches/{matchId}/hosts` — null when the room has no host (id 0).</summary>
public sealed record MatchHostView(UserBrief? Host);

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
public sealed record SlotView(UserBrief? User, string? Team, bool Locked);

/// <summary>
///     Payload for `GET/PUT/PATCH /matches/{matchId}/slots` — every slot 0-15 always present as a
///     dict key (JSON-serializes as string keys), per the owner's RFC 7396-friendly dict-over-array
///     design for every multiplayer slots representation.
/// </summary>
public sealed record MatchSlotsView(IReadOnlyDictionary<int, SlotView> Slots);
