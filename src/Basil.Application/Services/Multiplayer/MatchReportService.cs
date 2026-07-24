using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Users;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Services.Multiplayer;

public sealed class MatchReportService(
    IMatchRegistry matchRegistry,
    IMatchPersistenceRepository matchPersistence,
    IScoreRepository scores,
    IPlayerSessionRegistry sessionRegistry,
    IUserRepository users,
    IMapRepository maps)
{
    public async Task<MatchReport?> BuildAsync(int matchId, CancellationToken cancellationToken = default)
    {
        var matchRow = await matchPersistence.FetchMatchAsync(matchId, cancellationToken);
        if (matchRow is null) return null;

        var roundRows = await matchPersistence.FetchRoundsAsync(matchId, cancellationToken);
        var rounds = new List<MatchReportRound>(roundRows.Count);
        foreach (var round in roundRows)
        {
            var roundScores = await scores.FetchByRoundIdAsync(round.Id, cancellationToken);
            rounds.Add(await BuildRound(round, roundScores, cancellationToken));
        }

        var events = await matchPersistence.FetchEventsAsync(matchId, cancellationToken);
        var reportEvents = new List<MatchReportEvent>(events.Count);
        foreach (var e in events)
        {
            var actor = e.ActorUserId is { } actorId
                ? await MatchLiveSnapshotBuilder.ResolveOrPlaceholder(actorId, sessionRegistry, users, cancellationToken)
                : null;
            var target = e.TargetUserId is { } targetId
                ? await MatchLiveSnapshotBuilder.ResolveOrPlaceholder(targetId, sessionRegistry, users, cancellationToken)
                : null;
            reportEvents.Add(new MatchReportEvent(
                e.EventType, ((MatchEventType)e.EventType).ToString(), actor, target, e.Timestamp, e.Detail));
        }

        var live = matchRegistry.GetByDbId(matchId);
        MatchReportLiveInfo? liveInfo = null;
        if (live is not null)
        {
            var host = live.HostId > 0
                ? await MatchLiveSnapshotBuilder.ResolveOrPlaceholder(live.HostId, sessionRegistry, users, cancellationToken)
                : null;

            var referees = new List<UserBrief>();
            foreach (var id in live.Referees)
                referees.Add(await MatchLiveSnapshotBuilder.ResolveOrPlaceholder(id, sessionRegistry, users, cancellationToken));

            var liveSlots = new Dictionary<int, MatchLiveSlot>();
            for (var index = 0; index < live.Slots.Count; index++)
            {
                var slot = live.Slots[index];
                var user = slot.PlayerId is { } pid
                    ? await MatchLiveSnapshotBuilder.ResolveOrPlaceholder(pid, sessionRegistry, users, cancellationToken)
                    : null;
                liveSlots[index] = new MatchLiveSlot(user, slot.Status.ToString(), slot.Team.ToString(), (int)slot.Mods);
            }

            var liveBeatmap = await MatchLiveSnapshotBuilder.ResolveBeatmapAsync(live.MapMd5, maps, cancellationToken);

            liveInfo = new MatchReportLiveInfo(
                host, referees, liveSlots, liveBeatmap,
                live.MapId, live.MapMd5, live.Mode, live.WinCondition, live.TeamType,
                (int)live.Mods, live.Freemods, live.InProgress);
        }

        return new MatchReport(
            matchRow.Id, matchRow.Name, matchRow.CreatedAt, matchRow.EndedAt,
            liveInfo, reportEvents.ToArray(), rounds.ToArray());
    }

    private async Task<MatchReportRound> BuildRound(RoundRow round, IReadOnlyList<RoundScoreRow> roundScores,
        CancellationToken cancellationToken)
    {
        int? winnerUserId = null;
        string? winnerTeam = null;
        var winMetric = round.WinCondition switch
        {
            MatchWinCondition.Score => "score",
            MatchWinCondition.Accuracy => "accuracy",
            MatchWinCondition.Combo => "combo",
            MatchWinCondition.ScoreV2 => "scorev2",
            _ => "score"
        };
        long? winDiff = null;

        if (roundScores.Count == 0)
        {
            // No players
        }
        else if (roundScores.Count == 1)
        {
            // One player — they win by default, diff = 0
            var only = roundScores[0];
            if (roundScores.Any(s => s.Team is not null and not MatchTeam.Neutral))
            {
                winnerTeam = (only.Team ?? MatchTeam.Neutral).ToString();
                winnerUserId = only.UserId;
            }
            else
            {
                winnerUserId = only.UserId;
            }
            winDiff = 0;
        }
        else if (roundScores.Any(s => s.Team is not null and not MatchTeam.Neutral))
        {
            // Team mode (≥2 players)
            var teams = roundScores
                .Where(s => s.Team is not null && s.Team != MatchTeam.Neutral)
                .GroupBy(s => s.Team)
                .ToList();

            if (teams.Count < 2)
            {
                // Only one team has players — that team wins, diff = 0
                winnerTeam = (teams[0].Key ?? MatchTeam.Neutral).ToString();
                winDiff = 0;
            }
            else
            {
                var sorted = teams
                    .Select(g => new
                    {
                        Team = g.Key,
                        Total = g.Sum(s => GetMetric(s, round.WinCondition)),
                        Players = g.ToList()
                    })
                    .OrderByDescending(t => t.Total)
                    .ToList();

                if (sorted[0].Total == sorted[1].Total)
                {
                    // Draw — no winner, diff = 0
                    winDiff = 0;
                }
                else
                {
                    winnerTeam = (sorted[0].Team ?? MatchTeam.Neutral).ToString();
                    winDiff = sorted[0].Total - sorted[1].Total;
                }
            }
        }
        else
        {
            // Individual mode (≥2 players)
            var sorted = roundScores
                .Select(s => new { s.UserId, Metric = GetMetric(s, round.WinCondition) })
                .OrderByDescending(s => s.Metric)
                .ToList();

            if (sorted[0].Metric == sorted[1].Metric)
            {
                // Draw — no winner, diff = 0
                winDiff = 0;
            }
            else
            {
                winnerUserId = sorted[0].UserId;
                winDiff = sorted[0].Metric - sorted[1].Metric;
            }
        }

        var winner = winnerUserId is { } wid
            ? await MatchLiveSnapshotBuilder.ResolveOrPlaceholder(wid, sessionRegistry, users, cancellationToken)
            : null;

        var reportScores = new List<MatchReportScore>(roundScores.Count);
        foreach (var s in roundScores)
        {
            var user = await UserBriefResolver.ResolveAsync(s.UserId, sessionRegistry, users, cancellationToken)
                       ?? new UserBrief(s.UserId, s.UserName, Country.Xx.ToAcronym());
            reportScores.Add(new MatchReportScore(
                user, s.Team?.ToString(),
                (int)s.Mods, s.Score, s.Accuracy, s.MaxCombo,
                s.N300, s.N100, s.N50, s.NMiss, s.NGeki, s.NKatu,
                s.Grade, s.Perfect, s.SubmittedAt));
        }

        var beatmap = await MatchLiveSnapshotBuilder.ResolveBeatmapAsync(round.MapMd5, maps, cancellationToken);

        return new MatchReportRound(
            round.RoundIndex, round.MapMd5, beatmap,
            (int)round.Mode, (int)round.WinCondition, (int)round.TeamType,
            (int)round.Mods, round.Aborted, round.StartedAt, round.EndedAt,
            winner, winnerTeam,
            winMetric, winDiff, reportScores.ToArray());
    }

    private static long GetMetric(RoundScoreRow s, MatchWinCondition winCondition)
    {
        return winCondition switch
        {
            MatchWinCondition.Accuracy => (long)(s.Accuracy * 1000), // preserve 3 decimal places
            MatchWinCondition.Combo => s.MaxCombo,
            _ => s.Score
        };
    }
}

/// <summary>The TRT (match report) DTO.</summary>
public sealed record MatchReport(
    int MatchId,
    string Name,
    DateTime CreatedAt,
    DateTime? EndedAt,
    MatchReportLiveInfo? Live,
    MatchReportEvent[] Events,
    MatchReportRound[] Rounds);

/// <summary>Live room state — null when match is closed.</summary>
public sealed record MatchReportLiveInfo(
    UserBrief? Host,
    IReadOnlyList<UserBrief> Referees,
    IReadOnlyDictionary<int, MatchLiveSlot> Slots,
    Beatmap? Beatmap,
    int CurrentMapId,
    string CurrentMapMd5,
    GameMode Mode,
    MatchWinCondition WinCondition,
    MatchTeamType TeamType,
    int Mods,
    bool Freemods,
    bool InProgress);

/// <summary>One match lifecycle event.</summary>
public sealed record MatchReportEvent(
    int EventType,
    string EventTypeName,
    UserBrief? Actor,
    UserBrief? Target,
    DateTime Timestamp,
    string? Detail);

/// <summary>
///     One beatmap played within the match. Only `MapMd5` is stored on the underlying Round row —
///     `Beatmap` is resolved live at report-build time via the cached `IMapRepository`, null once
///     that md5 no longer resolves (content changed/removed since).
/// </summary>
public sealed record MatchReportRound(
    int RoundIndex,
    string MapMd5,
    Beatmap? Beatmap,
    int Mode,
    int WinCondition,
    int TeamType,
    int Mods,
    bool Aborted,
    DateTime StartedAt,
    DateTime? EndedAt,
    UserBrief? Winner,
    string? WinnerTeam,
    string? WinMetric,
    long? WinDiff,
    MatchReportScore[] Scores);

public sealed record MatchReportScore(
    UserBrief User,
    string? Team,
    int Mods,
    long Score,
    double Acc,
    int MaxCombo,
    int N300,
    int N100,
    int N50,
    int NMiss,
    int NGeki,
    int NKatu,
    string Grade,
    bool Perfect,
    DateTime SubmittedAt);
