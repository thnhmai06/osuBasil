namespace Basil.Web.Routing;

/// <summary>
///     Response shape for every paginated list route on the `api.` host: `{ page, pageSize, count,
///     hasMore, items }` — deliberately no `total`/`totalPages` (a separate COUNT query per request
///     isn't worth it for a private tournament server's data volumes). <see cref="HasMore" /> is
///     computed via the "overquery by one" trick: a caller fetches <see cref="PageSize" /> + 1 rows
///     and passes them to <see cref="Pagination.Trim{T}" />, which trims the extra row off and uses
///     its mere presence as the "is there another page" signal.
/// </summary>
public sealed record PagedResult<T>(int Page, int PageSize, int Count, bool HasMore, IReadOnlyList<T> Items);

public static class Pagination
{
    public const int DefaultPageSize = 50;

    /// <summary>1-based page, defaulting to 1/50 for missing or non-positive query values.</summary>
    public static (int Page, int PageSize) Normalize(int? page, int? pageSize)
    {
        return (page is > 0 ? page.Value : 1, pageSize is > 0 ? pageSize.Value : DefaultPageSize);
    }

    /// <summary>
    ///     Trims an "overqueried by one" source (fetched with <c>LIMIT pageSize + 1</c>) down to at
    ///     most <paramref name="pageSize" /> items, using the presence of that extra row as
    ///     <see cref="PagedResult{T}.HasMore" /> instead of a separate COUNT query.
    /// </summary>
    public static PagedResult<T> Trim<T>(IReadOnlyList<T> overqueried, int page, int pageSize)
    {
        var hasMore = overqueried.Count > pageSize;
        var items = hasMore ? overqueried.Take(pageSize).ToList() : overqueried;
        return new PagedResult<T>(page, pageSize, items.Count, hasMore, items);
    }
}
