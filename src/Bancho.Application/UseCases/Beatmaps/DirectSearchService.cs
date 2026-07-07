using Bancho.Application.Abstractions.Beatmaps;
using Bancho.Domain.Beatmaps;

namespace Bancho.Application.UseCases.Beatmaps;

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
    private static readonly string[] NonTextQueries = ["Newest", "Top+Rated", "Most+Played"];

    public async Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(
        DirectSearchRequest request, CancellationToken cancellationToken = default)
    {
        var queryText = NonTextQueries.Contains(request.Query) ? null : request.Query;
        GameMode? mode = request.Mode == -1 ? null : (GameMode)request.Mode;
        RankedStatus? status = request.RankedStatusArg == 4
            ? null
            : RankedStatusExtensions.FromOsuDirect(request.RankedStatusArg);

        return await maps.SearchAsync(queryText, mode, status, request.PageNum * 100, 100, cancellationToken);
    }
}