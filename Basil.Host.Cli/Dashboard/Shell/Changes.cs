namespace Basil.Host.Cli.Dashboard.Shell;

internal sealed class Changes
{
	public event Action? Occurred;

	public void Raise()
	{
		Occurred?.Invoke();
	}
}