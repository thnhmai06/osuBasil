using OpenOsuTournament.Bancho.Application.Abstractions.Multiplayer;
using OpenOsuTournament.Bancho.Application.Abstractions.Scores;
using OpenOsuTournament.Bancho.Application.Sessions.Multiplayer;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Multiplayer;

namespace OpenOsuTournament.Bancho.Application.UseCases.Multiplayer;

/// <summary>
///     Builds the TRT ("biên bản trận đấu" — match report) the user asked for: no dedicated table,
///     assembled on read from Matches/Rounds/Scores plus, for a room that's still open, the live
///     <see cref="MatchSession" /> in the registry. A round's Scores rows populate as soon as each
///     player submits (score submission links to <see cref="MatchSession.CurrentRoundId" /> directly —
///     see ScoreSubmissionUseCase's doc comment), so an in-progress round's partial results show up
///     here too, not just completed ones.
/// </summary>
public sealed class MatchReportService(
    IMatchRegistry matchRegistry,
    IMatchPersistenceRepository matchPersistence,
    IScoreRepository scores)
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

        var live = matchRegistry.GetByDbId(matchId);
        MatchReportSlot[]? liveSlots = null;
        int? currentMapId = null;
        if (live is not null)
        {
            liveSlots = live.Slots
                .Select((slot, index) => new MatchReportSlot(index, slot.PlayerId, slot.Status.ToString(),
                    slot.Team.ToString(), (int)slot.Mods))
                .ToArray();
            currentMapId = live.MapId;
        }

        return new MatchReport(
            matchRow.Id, matchRow.Name, (GameMode)matchRow.Mode, (MatchWinConditions)matchRow.WinCondition,
            (MatchTeamTypes)matchRow.TeamType, matchRow.HostId, matchRow.CreatedAt, matchRow.EndedAt,
            live is not null, liveSlots, currentMapId, rounds.ToArray());
    }

    private static MatchReportRound BuildRound(RoundRow round, IReadOnlyList<RoundScoreRow> roundScores)
    {
        int? winnerUserId = null;
        string? winnerTeam = null;

        if (roundScores.Count > 0)
        {
            if (roundScores.Any(s => s.Team is not null and not (int)MatchTeams.Neutral))
            {
                var topTeam = roundScores
                    .GroupBy(s => s.Team)
                    .OrderByDescending(g => g.Sum(s => s.Score))
                    .First();
                winnerTeam = ((MatchTeams)(topTeam.Key ?? (int)MatchTeams.Neutral)).ToString();
            }
            else
            {
                winnerUserId = roundScores.OrderByDescending(s => s.Score).First().UserId;
            }
        }

        var reportScores = roundScores.Select(s => new MatchReportScore(
                s.UserId, s.UserName, s.Team is null ? null : ((MatchTeams)s.Team.Value).ToString(), s.Mods,
                s.Score, s.Acc, s.MaxCombo, s.N300, s.N100, s.N50, s.NMiss, s.NGeki, s.NKatu, s.Grade, s.Perfect))
            .ToArray();

        return new MatchReportRound(
            round.RoundIndex, round.BeatmapId, round.MapMd5, round.Mods, round.StartedAt, round.EndedAt,
            winnerUserId, winnerTeam, reportScores);
    }
}

/// <summary>The TRT (match report) DTO — see <see cref="MatchReportService" />.</summary>
public sealed record MatchReport(
    int MatchId,
    string Name,
    GameMode Mode,
    MatchWinConditions WinCondition,
    MatchTeamTypes TeamType,
    int HostId,
    DateTime CreatedAt,
    DateTime? EndedAt,
    bool IsLive,
    MatchReportSlot[]? LiveSlots,
    int? CurrentMapId,
    MatchReportRound[] Rounds);

/// <summary>One of a live match's 16 slots. Null (not present) once the match is no longer live.</summary>
public sealed record MatchReportSlot(int SlotIndex, int? UserId, string Status, string Team, int Mods);

/// <summary>One beatmap played within the match. Winner fields are both null until any score lands.</summary>
public sealed record MatchReportRound(
    int RoundIndex,
    int BeatmapId,
    string MapMd5,
    int Mods,
    DateTime StartedAt,
    DateTime? EndedAt,
    int? WinnerUserId,
    string? WinnerTeam,
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
    bool Perfect);
