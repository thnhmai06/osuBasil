using XenoAtom.Terminal.UI;

namespace Basil.Host.Cli.Dashboard.Shell;

internal sealed record DetailView(string Title, IReadOnlyList<(string Label, string Value, Ink Kind)> Fields,
	IReadOnlyList<DetailAction> Actions, Func<Action<DetailView>, Visual>? Body = null)
{
	public Func<Task<DetailView>>? Reload { get; init; }
}
