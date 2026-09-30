using Basil.Application.Common.Queries;

namespace Basil.Application.Common.Persistence;

/// <summary>
///     Lets a repository be searched by a filter query, in addition to being read and written by key.
/// </summary>
/// <remarks>
///     Matches the filter semantics of the <see cref="Query{TFor}" /> passed in exactly.
/// </remarks>
/// <typeparam name="TValue">The type of item searched.</typeparam>
public interface ISearchable<TValue> where TValue : notnull
{
	/// <summary>Searches for items matching <paramref name="query" />.</summary>
	/// <param name="query">The filter to match.</param>
	/// <param name="options">The paging options to apply, or <see langword="null" /> for the default page.</param>
	/// <param name="sortOptions">The sort order to apply, or <see langword="null" /> for the default order.</param>
	/// <param name="cancellationToken">A token that cancels the search.</param>
	/// <returns>The matching items of the requested page, in the requested order.</returns>
	Task<IReadOnlyList<TValue>> SearchAsync(Query<TValue> query, QueryOptions? options = null,
		SortOptions? sortOptions = null,
		CancellationToken cancellationToken = default);

	/// <summary>Counts the items matching <paramref name="query" />.</summary>
	/// <param name="query">The filter to match.</param>
	/// <param name="cancellationToken">A token that cancels the count.</param>
	/// <returns>The number of matching items.</returns>
	Task<int> CountAsync(Query<TValue> query, CancellationToken cancellationToken = default);
}