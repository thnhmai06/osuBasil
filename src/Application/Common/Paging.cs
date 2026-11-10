namespace Basil.Application.Common;

/// <summary>Reads every item of a paged listing.</summary>
public static class Paging
{
	/// <summary>The number of items read per page.</summary>
	public const int PageSize = 100;

	/// <summary>Reads every page of a listing, from the first, until a page comes back short.</summary>
	/// <param name="list">Reads one page.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>Every item, in listing order.</returns>
	public static async Task<IReadOnlyList<T>> ListAllAsync<T>(Func<PageRequest, Task<Page<T>>> list,
		CancellationToken ct = default)
	{
		var page = new PageRequest(0, PageSize);
		var all = new List<T>();

		while (true)
		{
			ct.ThrowIfCancellationRequested();
			var paged = await list(page);
			all.AddRange(paged.Items);
			if (!paged.HasNext)
				break;
			page = new PageRequest(page.Offset + page.Limit, page.Limit);
		}

		return all;
	}
}