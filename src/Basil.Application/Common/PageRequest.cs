namespace Basil.Application.Common;

/// <summary>Which part of a listing to return.</summary>
public sealed record PageRequest
{
	/// <summary>Initializes a request for the items starting at an offset.</summary>
	/// <param name="offset">How many items to skip.</param>
	/// <param name="limit">The most items to return.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="offset" /> is negative or <paramref name="limit" /> is not positive.</exception>
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
}