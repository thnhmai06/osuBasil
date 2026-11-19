using XenoAtom.Terminal.UI;

namespace Basil.Host.Cli.Dashboard.Shell;

internal interface ITabView
{
	Visual View { get; }

	void Attach(TerminalApp app);

	void Activate();

	void Tick();

	void Refresh();
}

internal interface ISelectable
{
	void Select(string id);
}

internal interface ISearchable
{
	void FocusSearch();

	void ShowHelp();
}