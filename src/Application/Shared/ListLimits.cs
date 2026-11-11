namespace Basil.Application.Shared;

/// <summary>The ceilings that keep a listing without a page from reading an unbounded number of rows.</summary>
public static class ListLimits
{
	/// <summary>
	///     The most rows a listing that returns a whole collection reads; when the collection is larger, the rows beyond
	///     it in the documented order are left out.
	/// </summary>
	public const int Ceiling = 10_000;
}