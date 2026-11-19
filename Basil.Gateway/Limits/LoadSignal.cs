namespace Basil.Gateway.Limits;

public sealed class LoadSignal
{
	private double _value;

	public double Value => Volatile.Read(ref _value);

	public void Report(double used, double capacity)
	{
		Volatile.Write(ref _value, capacity <= 0 ? 0 : Math.Clamp(used / capacity, 0, 1));
	}
}