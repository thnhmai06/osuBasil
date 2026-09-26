using Basil.Application.Models.Queries;

namespace Basil.Application.Contracts.Repositories;

/// <summary>
///     Lets a repository be searched by a filter query, in addition to being read and written by key.
/// </summary>
/// <remarks>
///     Matches the filter semantics of the <see cref="Query{TFor}" /> passed in exactly.
/// </remarks>
/// <typeparam name="T">The type of item searched.</typeparam>
public interface ISearchable<T> where T : notnull
{
	/// <summary>Searches for items matching <paramref name="query" />.</summary>
	/// <param name="query">The filter to match.</param>
	/// <param name="options">The paging options to apply, or <see langword="null" /> for the default page.</param>
	/// <param name="sortOptions">The sort order to apply, or <see langword="null" /> for the default order.</param>
	/// <param name="cancellationToken">A token that cancels the search.</param>
	/// <returns>The matching items.</returns>
	IAsyncEnumerable<T> SearchAsync(Query<T> query, QueryOptions? options = null, SortOptions? sortOptions = null,
		CancellationToken cancellationToken = default);

	/// <summary>Counts the items matching <paramref name="query" />.</summary>
	/// <param name="query">The filter to match.</param>
	/// <param name="cancellationToken">A token that cancels the count.</param>
	/// <returns>The number of matching items.</returns>
	Task<int> CountAsync(Query<T> query, CancellationToken cancellationToken = default);
}