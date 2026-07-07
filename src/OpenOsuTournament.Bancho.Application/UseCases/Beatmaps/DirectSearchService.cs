using OpenOsuTournament.Bancho.Application.Abstractions.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Beatmaps;

namespace OpenOsuTournament.Bancho.Application.UseCases.Beatmaps;

/// <summary>Ported from app/services/direct_search.py's DirectSearchParams/search signature.</summary>
public sealed record DirectSearchRequest(string Query, int Mode, int RankedStatusArg, int PageNum);

/// <summary>
///     Ported from app/services/direct_search.py's DirectSearchService, replumbed to query the local
///     `maps` table instead of proxying a mirror API (osu-search.php runs fully offline now). The
///     mirror-error result code goes away with it — this server never talks to a mirror. The
///     "|"-in-metadata replacement quirk (DirectSearchResponseFormatter) stays: it's not
///     mirror-specific, it protects the pipe-delimited wire format from any locally-stored
///     artist/title/diffname that happens to contain a literal "|".
/// </summary>
public sealed class DirectSearchService(IMapRepository maps)
{
    /// <summary>
    ///     Shared with <see cref="DirectSearchResponseFormatter" /> — a full page signals "there may
    ///     be more" to the client (reported as 101 rather than the literal count).
    /// </summary>
    internal const int PageSize = 100;

    /// <summary>Client sentinel for "any mode" in <see cref="DirectSearchRequest.Mode" />.</summary>
    private const int AnyMode = -1;

    /// <summary>Client sentinel for "any ranked status" in <see cref="DirectSearchRequest.RankedStatusArg" />.</summary>
    private const int AnyRankedStatus = 4;

    private static readonly string[] NonTextQueries = ["Newest", "Top+Rated", "Most+Played"];

    public async Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(
        DirectSearchRequest request, CancellationToken cancellationToken = default)
    {
        var queryText = NonTextQueries.Contains(request.Query) ? null : request.Query;
        GameMode? mode = request.Mode == AnyMode ? null : (GameMode)request.Mode;
        RankedStatus? status = request.RankedStatusArg == AnyRankedStatus
            ? null
            : RankedStatusExtensions.FromOsuDirect(request.RankedStatusArg);

        return await maps.SearchAsync(queryText, mode, status, request.PageNum * PageSize, PageSize,
            cancellationToken);
    }
}