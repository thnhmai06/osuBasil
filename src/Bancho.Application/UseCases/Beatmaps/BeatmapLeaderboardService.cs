using Bancho.Application.Abstractions;
using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Domain;

namespace Bancho.Application.UseCases.Beatmaps;

public enum BeatmapLeaderboardResultCode
{
    Found,
    NeedsUpdate,
    NotSubmitted,
    NoLeaderboard,
}

/// <summary>
/// Ported from app/repositories/scores.py's PersonalBestLeaderboardScoreRow +
/// app/services/score_leaderboards.py's PersonalBestLeaderboardScoreListing, merged: UserId/Name
/// are always the requesting player's own (clan tag prefixing deferred until clans exist).
/// </summary>
public sealed record PersonalBestLeaderboardScoreListing(
    long Id, long Score, int MaxCombo, int N50, int N100, int N300, int NMiss, int NKatu, int NGeki,
    bool Perfect, int Mods, long Time, int Rank, int UserId, string Name);

/// <summary>
/// Ported from app/services/beatmap_leaderboards.py's BeatmapLeaderboardRequest. map_set_id and
/// aqn_files_found are dropped: map_set_id only existed to consult the whole-set osu!api cache
/// (removed along with the osu!api fallback — see EnsureBeatmapUseCase), and aqn_files_found only
/// fed an anti-cheat "strange occurrence" logger that doesn't exist yet (deferred to whichever
/// phase builds moderation logging).
/// </summary>
public sealed record BeatmapLeaderboardRequest(
    bool RequestingFromEditorSongSelect,
    LeaderboardType LeaderboardType,
    string MapMd5,
    string MapFilename,
    int ModeArg,
    int ModsArg);

/// <summary>Ported from app/services/beatmap_leaderboards.py's BeatmapLeaderboardResult.</summary>
public sealed record BeatmapLeaderboardResult(
    BeatmapLeaderboardResultCode Code,
    RankedStatus? RankedStatus = null,
    int? BeatmapId = null,
    int? BeatmapSetId = null,
    string? BeatmapName = null,
    double? BeatmapRating = null,
    IReadOnlyList<BeatmapLeaderboardScoreRow>? ScoreRows = null,
    PersonalBestLeaderboardScoreListing? PersonalBest = null);

/// <summary>
/// Ported from app/services/beatmap_leaderboards.py's BeatmapLeaderboardService, merged with
/// app/services/score_leaderboards.py's ScoreLeaderboardsService (the split existed in Python to
/// separate beatmap-gating from score-querying; bancho-net's simpler no-pp, no-clan, no-osu!api
/// scope doesn't carry enough independent complexity to justify keeping them as two classes).
/// Clan-tag prefixing on the personal-best display name is deferred until clans exist.
/// </summary>
public sealed class BeatmapLeaderboardService(
    EnsureBeatmapUseCase ensureBeatmap,
    IScoreRepository scores,
    IRatingRepository ratings,
    IRelationshipRepository relationships,
    IPlayerSessionRegistry sessionRegistry)
{
    public async Task<BeatmapLeaderboardResult> FetchLeaderboardAsync(
        PlayerSession player, BeatmapLeaderboardRequest request, CancellationToken cancellationToken = default)
    {
        var (mode, mods) = ResolveModeAndMods(request.ModeArg, request.ModsArg);
        UpdatePlayerStatusIfNeeded(player, mode, mods);

        var resolution = await ensureBeatmap.ResolveAsync(request.MapMd5, request.MapFilename, cancellationToken);
        if (resolution.Code == BeatmapResolutionResultCode.NotSubmitted)
        {
            return new BeatmapLeaderboardResult(BeatmapLeaderboardResultCode.NotSubmitted);
        }

        if (resolution.Code == BeatmapResolutionResultCode.NeedsUpdate)
        {
            return new BeatmapLeaderboardResult(BeatmapLeaderboardResultCode.NeedsUpdate);
        }

        var bmap = resolution.Beatmap!;
        if (!bmap.HasLeaderboard)
        {
            return new BeatmapLeaderboardResult(BeatmapLeaderboardResultCode.NoLeaderboard, RankedStatus: bmap.Status);
        }

        IReadOnlyList<BeatmapLeaderboardScoreRow> scoreRows = [];
        PersonalBestLeaderboardScoreListing? personalBest = null;

        if (!request.RequestingFromEditorSongSelect)
        {
            int? modsFilter = request.LeaderboardType == Domain.LeaderboardType.Mods ? (int)mods : null;

            IReadOnlySet<int>? friendIds = null;
            if (request.LeaderboardType == Domain.LeaderboardType.Friends)
            {
                var relations = await relationships.FetchAllAsync(player.Id, RelationshipType.Friend, cancellationToken);
                friendIds = relations.Select(r => r.User2).Append(player.Id).ToHashSet();
            }

            var country = request.LeaderboardType == Domain.LeaderboardType.Country ? player.Geoloc.CountryAcronym : null;

            scoreRows = await scores.FetchBeatmapLeaderboardScoresAsync(
                bmap.Md5, mode, player.Id, modsFilter, friendIds, country, cancellationToken: cancellationToken);

            if (scoreRows.Count > 0)
            {
                var best = await scores.FetchPersonalBestLeaderboardScoreAsync(bmap.Md5, mode, player.Id, cancellationToken);
                if (best is not null)
                {
                    var rank = await scores.FetchPersonalBestLeaderboardRankAsync(bmap.Md5, mode, best.Score, cancellationToken);
                    personalBest = new PersonalBestLeaderboardScoreListing(
                        best.Id, best.Score, best.MaxCombo, best.N50, best.N100, best.N300, best.NMiss,
                        best.NKatu, best.NGeki, best.Perfect, best.Mods, best.Time, rank, player.Id, player.Name);
                }
            }
        }

        var rating = await ratings.FetchAverageRatingAsync(bmap.Md5, cancellationToken);

        return new BeatmapLeaderboardResult(
            BeatmapLeaderboardResultCode.Found,
            RankedStatus: bmap.Status,
            BeatmapId: bmap.Id,
            BeatmapSetId: bmap.SetId,
            BeatmapName: bmap.FullName,
            BeatmapRating: rating,
            ScoreRows: scoreRows,
            PersonalBest: personalBest);
    }

    /// <summary>
    /// Ported from BeatmapLeaderboardService._resolve_score_query_mode_and_mods. Deliberately NOT
    /// reusing GameModeExtensions.FromParams — that helper checks Autopilot before Relax (matching
    /// GameMode.from_params), while this call site and ChangeActionHandler both check Relax first;
    /// this is a genuine divergence between call sites in the Python source, not a bug to unify.
    /// </summary>
    private static (GameMode Mode, Mods Mods) ResolveModeAndMods(int modeArg, int modsArg)
    {
        var mode = modeArg;
        var mods = modsArg;

        if ((mods & (int)Mods.Relax) != 0)
        {
            if (mode == 3) // rx!mania doesn't exist
            {
                mods &= ~(int)Mods.Relax;
            }
            else
            {
                mode += 4;
            }
        }
        else if ((mods & (int)Mods.Autopilot) != 0)
        {
            if (mode is 1 or 2 or 3) // ap!catch, taiko and mania don't exist
            {
                mods &= ~(int)Mods.Autopilot;
            }
            else
            {
                mode += 8;
            }
        }

        return ((GameMode)mode, (Mods)mods);
    }

    /// <summary>Ported from BeatmapLeaderboardService._update_player_status_if_needed.</summary>
    private void UpdatePlayerStatusIfNeeded(PlayerSession player, GameMode mode, Mods mods)
    {
        if (mode == player.Status.Mode)
        {
            return;
        }

        player.Status.Mode = mode;
        player.Status.Mods = mods;

        if (!player.Restricted)
        {
            var statsPacket = PacketBuilders.BuildUserStats(player);
            foreach (var other in sessionRegistry.All)
            {
                other.Enqueue(statsPacket);
            }
        }
    }
}
