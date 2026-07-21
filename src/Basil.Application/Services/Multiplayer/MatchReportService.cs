using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Scores;
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
    IPlayerSessionRegistry sessionRegistry)
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
            rounds.Add(BuildRound(round, roundScores));
        }

        var events = await matchPersistence.FetchEventsAsync(matchId, cancellationToken);
        var reportEvents = events.Select(e => new MatchReportEvent(
            e.EventType, ((MatchEventType)e.EventType).ToString(),
            e.ActorUserId, e.ActorUserName,
            e.TargetUserId, e.TargetUserName,
            e.Timestamp, e.Detail)).ToArray();

        var live = matchRegistry.GetByDbId(matchId);
        MatchReportLiveInfo? liveInfo = null;
        if (live is not null)
        {
            var host = live.HostId > 0 ? sessionRegistry.GetById(live.HostId) : null;
            var referees = live.Referees
                .Select(id =>
                {
                    var s = sessionRegistry.GetById(id);
                    return new UserBrief(id, s?.Name, s?.Geoloc.Country.ToAcronym());
                })
                .ToArray();
            var liveSlots = live.Slots
                .Select((slot, index) =>
                {
                    var s = slot.PlayerId is { } pid ? sessionRegistry.GetById(pid) : null;
                    return new MatchLiveSlot(index, slot.PlayerId, s?.Name, s?.Geoloc.Country.ToAcronym(),
                        slot.Status.ToString(), slot.Team.ToString(), (int)slot.Mods);
                })
                .ToArray();

            liveInfo = new MatchReportLiveInfo(
                host is not null ? new UserBrief(host.Id, host.Name, host.Geoloc.Country.ToAcronym()) : null,
                referees, liveSlots,
                live.MapId, live.MapMd5, live.Mode, live.WinCondition, live.TeamType,
                (int)live.Mods, live.Freemods, live.InProgress);
        }

        return new MatchReport(
            matchRow.Id, matchRow.Name, matchRow.CreatedAt, matchRow.EndedAt,
            liveInfo, reportEvents, rounds.ToArray());
    }

    private static MatchReportRound BuildRound(RoundRow round, IReadOnlyList<RoundScoreRow> roundScores)
    {
        int? winnerUserId = null;
        string? winnerUserName = null;
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
                winnerUserName = only.UserName;
            }
            else
            {
                winnerUserId = only.UserId;
                winnerUserName = only.UserName;
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
                .Select(s => new { s.UserId, s.UserName, Metric = GetMetric(s, round.WinCondition) })
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
                winnerUserName = sorted[0].UserName;
                winDiff = sorted[0].Metric - sorted[1].Metric;
            }
        }

        var reportScores = roundScores.Select(s => new MatchReportScore(
                s.UserId, s.UserName, s.Team?.ToString(),
                (int)s.Mods, s.Score, s.Accuracy, s.MaxCombo,
                s.N300, s.N100, s.N50, s.NMiss, s.NGeki, s.NKatu,
                s.Grade, s.Perfect, s.SubmittedAt))
            .ToArray();

        return new MatchReportRound(
            round.RoundIndex, round.BeatmapId, round.MapMd5,
            round.BeatmapArtist, round.BeatmapTitle, round.BeatmapVersion, round.BeatmapCreator,
            (int)round.Mode, (int)round.WinCondition, (int)round.TeamType,
            (int)round.Mods, round.Aborted, round.StartedAt, round.EndedAt,
            winnerUserId, winnerUserName, winnerTeam,
            winMetric, winDiff, reportScores);
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
    UserBrief[] Referees,
    MatchLiveSlot[] Slots,
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
    int? ActorUserId,
    string? ActorUserName,
    int? TargetUserId,
    string? TargetUserName,
    DateTime Timestamp,
    string? Detail);

/// <summary>One beatmap played within the match.</summary>
public sealed record MatchReportRound(
    int RoundIndex,
    int BeatmapId,
    string MapMd5,
    string BeatmapArtist,
    string BeatmapTitle,
    string BeatmapVersion,
    string BeatmapCreator,
    int Mode,
    int WinCondition,
    int TeamType,
    int Mods,
    bool Aborted,
    DateTime StartedAt,
    DateTime? EndedAt,
    int? WinnerUserId,
    string? WinnerUserName,
    string? WinnerTeam,
    string? WinMetric,
    long? WinDiff,
    MatchReportScore[] Scores);

public sealed record MatchReportScore(
    int UserId,
    string UserName,
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
