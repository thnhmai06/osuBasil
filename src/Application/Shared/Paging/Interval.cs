namespace Basil.Application.Shared.Paging;

public readonly record struct Interval<T>(T? Min, T? Max, bool MinInclusive = true, bool MaxInclusive = true)
	where T : struct, IComparable<T>
{
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