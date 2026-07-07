namespace OpenOsuTournament.Bancho.Application.Abstractions.Beatmaps;

/// <summary>
///     Ported from app/repositories/ratings.py, scoped to what the getscores response header needs:
///     the average player-submitted rating for a beatmap. Rating submission (POST /web/osu-rate.php)
///     is out of scope for Phase 5 — deferred to whichever later phase ports the remaining osu-web
///     endpoints.
/// </summary>
public interface IRatingRepository
{
    /// <summary>Ported from BeatmapLeaderboardService._fetch_map_rating_average. Returns 0.0 when no ratings exist.</summary>
    Task<double> FetchAverageRatingAsync(string mapMd5, CancellationToken cancellationToken = default);
}