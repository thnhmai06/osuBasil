namespace Basil.Gateway;

/// <summary>How loaded the server is, as a fraction between 0 (idle) and 1 (at its ceiling).</summary>
public sealed class LoadSignal
{
	private double _value;

	/// <summary>Gets the latest reported load, between 0 and 1.</summary>
	public double Value => Volatile.Read(ref _value);

	/// <summary>Replaces the load with a fraction of a capacity.</summary>
	/// <param name="used">How much of the capacity is in use.</param>
	/// <param name="capacity">The capacity; a capacity of zero or less reports no load.</param>
	public void Report(double used, double capacity)
	{
		Volatile.Write(ref _value, capacity <= 0 ? 0 : Math.Clamp(used / capacity, 0, 1));
	}
}
