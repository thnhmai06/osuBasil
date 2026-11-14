namespace Basil.Application.Shared.Paging;

/// <summary>Which part of a listing to return.</summary>
public sealed record PageRequest
{
	/// <summary>Initializes a request for the items starting at an offset.</summary>
	/// <param name="offset">How many items to skip.</param>
	/// <param name="limit">The most items to return.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="offset" /> is negative or <paramref name="limit" /> is
	///     not positive.
	/// </exception>
	public PageRequest(int offset, int limit)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
		Offset = offset;
		Limit = limit;
	}

	/// <summary>Gets how many items to skip.</summary>
	public int Offset { get; }

	/// <summary>Gets the most items to return.</summary>
	public int Limit { get; }

	/// <summary>Gets how many items a listing reads to fill the page and learn whether another follows.</summary>
	public int Fetch => Limit + 1;

	/// <summary>Cuts what a listing read with <see cref="Fetch" /> down to the page.</summary>
	/// <param name="fetched">The items read, at most <see cref="Fetch" /> of them.</param>
	/// <typeparam name="T">The type of the items.</typeparam>
	/// <returns>The first <see cref="Limit" /> items, and whether an item was left over.</returns>
	public (IReadOnlyList<T> Items, bool HasNext) Slice<T>(IReadOnlyList<T> fetched)
	{
		return fetched.Count > Limit ? ([.. fetched.Take(Limit)], true) : (fetched, false);
	}
}