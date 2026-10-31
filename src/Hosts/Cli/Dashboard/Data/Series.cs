namespace Basil.Host.Cli.Dashboard.Data;

/// <summary>A series of samples that keeps the most recent ones: a ring buffer.</summary>
/// <param name="capacity">How many samples are kept.</param>
public sealed class Series(int capacity)
{
	private readonly Lock _gate = new();
	private readonly double[] _values = new double[capacity];
	private int _count;
	private int _next;

	/// <summary>Gets how many samples are kept.</summary>
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

	/// <summary>Gets the newest sample, or 0 when there is none.</summary>
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

	/// <summary>Adds a sample, dropping the oldest when the buffer is full.</summary>
	public void Add(double value)
	{
		lock (_gate)
		{
			_values[_next] = value;
			_next = (_next + 1) % capacity;
			_count = Math.Min(_count + 1, capacity);
		}
	}

	/// <summary>Gets the samples, oldest first.</summary>
	public double[] Values()
	{
		lock (_gate)
		{
			var result = new double[_count];
			for (var i = 0; i < _count; i++) result[i] = _values[(_next - _count + i + capacity) % capacity];
			return result;
		}
	}

	/// <summary>Gets the largest sample, or 0 when there is none.</summary>
	public double Max()
	{
		var values = Values();
		return values.Length == 0 ? 0 : values.Max();
	}
}