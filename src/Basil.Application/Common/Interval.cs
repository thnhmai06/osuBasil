namespace Basil.Application.Common;

/// <summary>A range of values; a missing bound leaves that side open.</summary>
/// <param name="Min">The lower bound, or <see langword="null" /> for none.</param>
/// <param name="Max">The upper bound, or <see langword="null" /> for none.</param>
/// <param name="MinInclusive">Whether a value equal to <paramref name="Min" /> is in the range.</param>
/// <param name="MaxInclusive">Whether a value equal to <paramref name="Max" /> is in the range.</param>
/// <typeparam name="T">The type of the values.</typeparam>
public readonly record struct Interval<T>(T? Min, T? Max, bool MinInclusive = true, bool MaxInclusive = true)
	where T : struct, IComparable<T>
{
	/// <summary>Gets a value that indicates whether a value lies in the range.</summary>
	/// <param name="value">The value to test.</param>
	public bool Contains(T value)
	{
		if (Min is not null)
		{
			var cmp = value.CompareTo(Min.Value);
			if (MinInclusive ? cmp < 0 : cmp <= 0) return false;
		}
		if (Max is not null)
		{
			var cmp = value.CompareTo(Max.Value);
			if (MaxInclusive ? cmp > 0 : cmp >= 0) return false;
		}
		return true;
	}
}