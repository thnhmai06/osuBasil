namespace Basil.Web.Routing;

/// <summary>
///     Non-generic marker <see cref="PagedResult{T}" /> implements, so the enveloping layer (both the
///     runtime <see cref="Basil.Web.Middleware.EnvelopeMiddleware" /> and the OpenAPI example wrapper
///     in <see cref="Basil.Web.OpenApi.OpenApiExampleExtensions" />) can recognize a paginated body and
///     split it into `data`/`meta` without needing to know the item type <c>T</c>.
/// </summary>
public interface IPagedResult
{
	/// <summary>Gets the current page number, 1-based.</summary>
	int Page { get; }

	/// <summary>Gets the number of items per page.</summary>
	int PageSize { get; }

	/// <summary>Gets the total number of records across all pages.</summary>
	int TotalRecords { get; }

	/// <summary>Gets the items as untyped objects, for the enveloping layer to split out.</summary>
	IEnumerable<object?> ItemsUntyped { get; }
}

/// <summary>
///     Response shape for every paginated list route on the `api.` host. <see cref="TotalRecords" />
///     backs the Enveloped Response Standard's `meta.totalRecords`/`meta.totalPages`: for `GET /scores`
///     and `GET /beatmapsets` it comes from the cached `Counters` table (see each repository's
///     `FetchCountAsync`); for `GET /matches` (no counter table, see that route) it's simply the
///     count of the already-fully-materialized, already-filtered in-memory list before paging.
/// </summary>
public sealed record PagedResult<T>(int Page, int PageSize, int TotalRecords, IReadOnlyList<T> Items) : IPagedResult
{
	IEnumerable<object?> IPagedResult.ItemsUntyped => Items.Cast<object?>();
}

/// <summary>
///     Normalizes pagination query parameters and trims overqueried result sets into the
///     <see cref="PagedResult{T}" /> shape every paginated list route on the `api.` host returns.
/// </summary>
public static class Pagination
{
	/// <summary>The default page size when a request omits or sends a non-positive `pageSize`.</summary>
	public const int DefaultPageSize = 50;

	/// <summary>1-based page, defaulting to 1/50 for missing or non-positive query values.</summary>
	public static (int Page, int PageSize) Normalize(int? page, int? pageSize)
	{
		return (page is > 0 ? page.Value : 1, pageSize is > 0 ? pageSize.Value : DefaultPageSize);
	}

	/// <summary>
	///     Trims an "overqueried by one" source (fetched with <c>LIMIT pageSize + 1</c>) down to at
	///     most <paramref name="pageSize" /> items: the extra row is discarded now that
	///     <paramref name="totalRecords" /> (a real count) makes it unnecessary for anything.
	/// </summary>
	public static PagedResult<T> Trim<T>(IReadOnlyList<T> overqueried, int page, int pageSize, int totalRecords)
	{
		var items = overqueried.Count > pageSize ? [.. overqueried.Take(pageSize)] : overqueried;
		return new PagedResult<T>(page, pageSize, totalRecords, items);
	}
}