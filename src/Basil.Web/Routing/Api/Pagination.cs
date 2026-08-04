namespace Basil.Web.Routing.Api;
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global
/// <summary>
///     Non-generic shape implemented by <see cref="PagedResult{T}" />, so a caller can recognize a paginated body
///     without knowing its item type.
/// </summary>
public interface IPagedResult
{
	/// <summary>Gets the current page number, 1-based.</summary>
	int Page { get; }

	/// <summary>Gets the number of items per page.</summary>
	int PageSize { get; }

	/// <summary>Gets the total number of records across all pages.</summary>
	int TotalRecords { get; }

	/// <summary>Gets the items as untyped objects.</summary>
	IEnumerable<object?> ItemsUntyped { get; }
}

/// <summary>Response shape for every paginated list route on the `api.` host.</summary>
public sealed record PagedResult<T>(int Page, int PageSize, int TotalRecords, IReadOnlyList<T> Items) : IPagedResult
{
	IEnumerable<object?> IPagedResult.ItemsUntyped => Items.Cast<object?>();
}

/// <summary>
///     Normalizes pagination query parameters and trims result sets into the shape every paginated list route on the
///     `api.` host returns.
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

	/// <summary>Trims a result set down to at most <paramref name="pageSize" /> items.</summary>
	public static PagedResult<T> Trim<T>(IReadOnlyList<T> overqueried, int page, int pageSize, int totalRecords)
	{
		var items = overqueried.Count > pageSize ? [.. overqueried.Take(pageSize)] : overqueried;
		return new PagedResult<T>(page, pageSize, totalRecords, items);
	}
}