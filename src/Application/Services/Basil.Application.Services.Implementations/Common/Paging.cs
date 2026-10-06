using Basil.Application.Storage.Contracts.Common;

namespace Basil.Application.Services.Implementations.Common;

/// <summary>Reads every item of a paged listing.</summary>
internal static class Paging
{
	/// <summary>The number of items read per page.</summary>
	internal const int PageSize = 100;

	/// <summary>Reads every page of a listing, from the first, until a page comes back short.</summary>
	/// <param name="list">Reads one page.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Every item, in listing order.</returns>
	internal static async Task<List<T>> ListAllAsync<T>(Func<PageRequest, Task<Page<T>>> list,
		CancellationToken cancellationToken = default)
	{
		var page = new PageRequest(0, PageSize);
		var all = new List<T>();

		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var paged = await list(page);
			all.AddRange(paged.Items);
			if (paged.Items.Count < page.Limit)
				break;
			page = new PageRequest(page.Offset + page.Limit, page.Limit);
		}

		return all;
	}
}
