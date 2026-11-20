namespace Basil.Application.Shared.Paging;

public static class Paging
{
	public const int PageSize = 100;

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