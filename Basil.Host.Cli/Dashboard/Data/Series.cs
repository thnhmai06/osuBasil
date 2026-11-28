namespace Basil.Host.Cli.Dashboard.Data;

public sealed class Series(int capacity)
{
	private readonly Lock _gate = new();
	private readonly double[] _values = new double[capacity];
	private int _count;
	private int _next;

	public int Count
	{
		get
		{
			lock (_gate)
			{
				return _count;
			}
		}
	}

	public double Latest
	{
		get
		{
			lock (_gate)
			{
				return _count == 0 ? 0 : _values[(_next - 1 + capacity) % capacity];
			}
		}
	}

	public void Add(double value)
	{
		lock (_gate)
		{
			_values[_next] = value;
			_next = (_next + 1) % capacity;
			_count = Math.Min(_count + 1, capacity);
		}
	}

	public double[] Values()
	{
		lock (_gate)
		{
			var result = new double[_count];
			for (var i = 0; i < _count; i++) result[i] = _values[(_next - _count + i + capacity) % capacity];
			return result;
		}
	}

	public double Max()
	{
		var values = Values();
		return values.Length == 0 ? 0 : values.Max();
	}
}