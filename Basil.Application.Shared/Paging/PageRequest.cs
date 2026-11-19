namespace Basil.Application.Shared.Paging;

public sealed record PageRequest
{
	public PageRequest(int offset, int limit)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
		Offset = offset;
		Limit = limit;
	}

	public int Offset { get; }

	public int Limit { get; }

	public int Fetch => Limit + 1;

	public (IReadOnlyList<T> Items, bool HasNext) Slice<T>(IReadOnlyList<T> fetched)
	{
		return fetched.Count > Limit ? ([.. fetched.Take(Limit)], true) : (fetched, false);
	}
}