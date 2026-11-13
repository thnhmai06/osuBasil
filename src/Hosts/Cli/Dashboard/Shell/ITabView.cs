using XenoAtom.Terminal.UI;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>A tab of the dashboard that loads its own data.</summary>
internal interface ITabView
{
	/// <summary>Gets what the tab shows.</summary>
	Visual View { get; }

	/// <summary>Connects the tab to the running application.</summary>
	void Attach(TerminalApp app);

	/// <summary>Called when the tab comes into view; loads the data the first time.</summary>
	void Activate();

	/// <summary>Called about ten times a second while the tab is in view.</summary>
	void Tick();

	/// <summary>Loads the data again, keeping the selection (Ctrl+R).</summary>
	void Refresh();
}
