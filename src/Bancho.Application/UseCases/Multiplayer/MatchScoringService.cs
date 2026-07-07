using System.Text.RegularExpressions;
using Bancho.Application.Abstractions;
using Bancho.Application.Sessions;
using Bancho.Domain;

namespace Bancho.Application.UseCases.Multiplayer;

/// <summary>
/// Ported from Match.await_submissions + Match.update_matchpoints — scrim's winner-per-round
/// determination. Must be launched AFTER MATCH_COMPLETE's handler releases MatchSession.Lock (see
/// MatchSession's doc comment): this polls PlayerSession.RecentScore for up to 10s total (not per
/// player, matching the Python source's shared time budget across the whole was-playing list)
/// without holding any match lock, then re-acquires the lock only briefly at the end to record the
/// point and send the announcement.
///
/// Deviates from bancho.py in one place for safety: the Python source holds Slot references and
/// asserts `s.player is not None` mid-poll, which would crash if that player leaves the match
/// during the wait. This port snapshots player ids instead (see <see cref="MatchRoundSnapshot"/>)
/// and treats a since-logged-off player as "didn't submit" rather than crashing.
///
/// Also drops `use_pp_scoring` entirely, per the no-pp scope decision — always resolves the win
/// condition via `(score, acc, max_combo, score)[win_condition]`.
/// </summary>
public sealed partial class MatchScoringService(
    IMapRepository maps,
    IPlayerSessionRegistry sessionRegistry,
    MatchMembershipService matchMembership,
    IClock clock,
    TimeSpan? pollInterval = null,
    TimeSpan? pollTimeout = null)
{
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);
    private readonly TimeSpan _pollTimeout = pollTimeout ?? TimeSpan.FromSeconds(10);

    /// <summary>Entry point for MATCH_COMPLETE to fire-and-forget once its own lock is released.</summary>
    public async Task ScoreCompletedRoundAsync(MatchSession match, MatchRoundSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        try
        {
            await UpdateMatchPointsAsync(match, snapshot, cancellationToken);
        }
        catch (Exception)
        {
            await match.Lock.WaitAsync(cancellationToken);
            try
            {
                matchMembership.SendBot(match, "Scores could not be calculated.");
            }
            finally
            {
                match.Lock.Release();
            }
        }
    }

    private async Task UpdateMatchPointsAsync(MatchSession match, MatchRoundSnapshot snapshot, CancellationToken cancellationToken)
    {
        var (scores, didntSubmit) = await AwaitSubmissionsAsync(snapshot, cancellationToken);

        await match.Lock.WaitAsync(cancellationToken);
        try
        {
            foreach (var playerId in didntSubmit)
            {
                var name = sessionRegistry.GetById(playerId)?.Name ?? $"player #{playerId}";
                matchMembership.SendBot(match, $"{name} didn't submit a score (timeout: 10s).");
            }

            if (scores.Count == 0)
            {
                matchMembership.SendBot(match, "Scores could not be calculated.");
                return;
            }

            var ffa = snapshot.TeamType is MatchTeamTypes.HeadToHead or MatchTeamTypes.TagCoop;

            if (scores.Count != 1 && scores.Values.Distinct().Count() == 1)
            {
                match.RecordWinner(null);
                matchMembership.SendBot(match, "The point has ended in a tie!");
                return;
            }

            var winner = scores.MaxBy(kv => kv.Value).Key;
            match.RecordWinner(winner);
            match.AddMatchPoint(winner);

            var messages = ffa
                ? BuildFfaMessages(match, snapshot, scores, winner)
                : BuildTeamMessages(match, snapshot, scores, winner);

            if (didntSubmit.Count > 0)
            {
                matchMembership.SendBot(match, "If you'd like to perform a rematch, please use the `!mp rematch` command.");
            }

            foreach (var line in messages)
            {
                matchMembership.SendBot(match, line);
            }
        }
        finally
        {
            match.Lock.Release();
        }
    }

    private List<string> BuildFfaMessages(
        MatchSession match, MatchRoundSnapshot snapshot, Dictionary<ScrimParticipant, long> scores, ScrimParticipant winner)
    {
        var messages = new List<string>();
        var winnerName = sessionRegistry.GetById(winner.PlayerId!.Value)?.Name ?? $"player #{winner.PlayerId}";
        var average = scores.Values.Average();

        messages.Add($"{winnerName} takes the point! ({AddSuffix(scores[winner], snapshot.WinCondition)} " +
                      $"[Match avg. {AddSuffix((long)average, snapshot.WinCondition)}])");

        var winnerPoints = match.GetMatchPoints(winner);
        if (match.WinningPoints > 0 && winnerPoints == match.WinningPoints)
        {
            match.IsScrimming = false;
            match.ResetScrim();
            messages.Add($"{winnerName} takes the match! Congratulations!");
        }
        else
        {
            var ordered = match.MatchPoints.OrderBy(kv => kv.Value)
                .Select(kv => $"{(sessionRegistry.GetById(kv.Key.PlayerId!.Value)?.Name ?? $"player #{kv.Key.PlayerId}")} - {kv.Value}");
            messages.Add($"Total Score: {string.Join(" | ", ordered)}");
        }

        return messages;
    }

    private List<string> BuildTeamMessages(
        MatchSession match, MatchRoundSnapshot snapshot, Dictionary<ScrimParticipant, long> scores, ScrimParticipant winner)
    {
        var messages = new List<string>();
        var teamNameMatch = TourneyMatchNameRegex().Match(match.Name);
        string matchName;
        string blueName;
        string redName;
        if (teamNameMatch.Success)
        {
            matchName = teamNameMatch.Groups["name"].Value;
            blueName = teamNameMatch.Groups["T1"].Value;
            redName = teamNameMatch.Groups["T2"].Value;
        }
        else
        {
            matchName = match.Name;
            blueName = "Blue";
            redName = "Red";
        }

        var winnerTeam = winner.Team!.Value;
        var loserTeam = winnerTeam == MatchTeams.Blue ? MatchTeams.Red : MatchTeams.Blue;
        var loser = ScrimParticipant.OfTeam(loserTeam);

        var winnerName = winnerTeam == MatchTeams.Blue ? blueName : redName;
        var loserName = loserTeam == MatchTeams.Blue ? blueName : redName;

        var winnerScore = AddSuffix(scores.GetValueOrDefault(winner), snapshot.WinCondition);
        var loserScore = AddSuffix(scores.GetValueOrDefault(loser), snapshot.WinCondition);

        var winnerPoints = match.GetMatchPoints(winner);
        var loserPoints = match.GetMatchPoints(loser);

        messages.Add($"{winnerName} takes the point! ({winnerScore} vs. {loserScore})");

        if (match.WinningPoints > 0 && winnerPoints == match.WinningPoints)
        {
            match.IsScrimming = false;
            match.ResetScrim();
            messages.Add($"{winnerName} takes the match, finishing {matchName} with a score of {winnerPoints} - {loserPoints}! Congratulations!");
        }
        else
        {
            messages.Add($"Total Score: {winnerName} | {winnerPoints} - {loserPoints} | {loserName}");
        }

        return messages;
    }

    private static string AddSuffix(long score, MatchWinConditions winCondition) => winCondition switch
    {
        MatchWinConditions.Accuracy => $"{score:F2}%",
        MatchWinConditions.Combo => $"{score}x",
        _ => score.ToString(),
    };

    /// <summary>Ported from Match.await_submissions — a serial poll across `was_playing`, sharing one 10s budget for the whole list, not 10s per player.</summary>
    private async Task<(Dictionary<ScrimParticipant, long> Scores, List<int> DidntSubmit)> AwaitSubmissionsAsync(
        MatchRoundSnapshot snapshot, CancellationToken cancellationToken)
    {
        var scores = new Dictionary<ScrimParticipant, long>();
        var didntSubmit = new List<int>();
        var timeWaited = TimeSpan.Zero;
        var ffa = snapshot.TeamType is MatchTeamTypes.HeadToHead or MatchTeamTypes.TagCoop;

        var bmap = await maps.FetchOneAsync(md5: snapshot.MapMd5, cancellationToken: cancellationToken);
        if (bmap is null)
        {
            return (scores, didntSubmit);
        }

        foreach (var (playerId, team) in snapshot.WasPlaying)
        {
            while (true)
            {
                var player = sessionRegistry.GetById(playerId);
                var recent = player?.RecentScore;
                var maxAge = clock.UtcNow.UtcDateTime - TimeSpan.FromSeconds(bmap.TotalLength) - timeWaited - TimeSpan.FromMilliseconds(500);

                if (player is not null && recent is not null && recent.BeatmapMd5 == snapshot.MapMd5 && recent.ServerTime > maxAge)
                {
                    var value = snapshot.WinCondition switch
                    {
                        MatchWinConditions.Accuracy => (long)recent.Acc,
                        MatchWinConditions.Combo => recent.MaxCombo,
                        _ => recent.Score,
                    };

                    if (value != 0)
                    {
                        var key = ffa ? ScrimParticipant.OfPlayer(playerId) : ScrimParticipant.OfTeam(team);
                        scores[key] = scores.GetValueOrDefault(key) + value;
                    }

                    break;
                }

                if (player is null)
                {
                    didntSubmit.Add(playerId);
                    break;
                }

                await Task.Delay(_pollInterval, cancellationToken);
                timeWaited += _pollInterval;

                if (timeWaited > _pollTimeout)
                {
                    didntSubmit.Add(playerId);
                    break;
                }
            }
        }

        return (scores, didntSubmit);
    }

    [GeneratedRegex(@"^(?<name>[a-zA-Z0-9_ ]+): \((?<T1>[a-zA-Z0-9_ ]+)\) vs\.? \((?<T2>[a-zA-Z0-9_ ]+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex TourneyMatchNameRegex();
}
