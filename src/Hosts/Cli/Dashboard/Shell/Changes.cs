namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>Tells every page that something the server holds was changed by an action, so that they read it again.</summary>
internal sealed class Changes
{
	/// <summary>Occurs, on the thread of the interface, after an action that changed the server finished.</summary>
	public event Action? Occurred;

	/// <summary>Announces a change.</summary>
	public void Raise()
	{
		Occurred?.Invoke();
	}
}
