namespace Basil.Host.Cli.Dashboard.Shell;

internal sealed record DetailAction(string Label, Action Run, DetailActionKind Kind = DetailActionKind.Normal)
{
	public Func<DetailView>? Next { get; init; }

	public Func<Task<DetailView>>? NextAsync { get; init; }

	public IReadOnlyList<DetailAction>? Children { get; init; }

	public static DetailAction Menu(string label, IReadOnlyList<DetailAction> children)
	{
		return new DetailAction(label, () => { }) { Children = children };
	}

	public static DetailAction Open(string tab, Func<DetailView> next)
	{
		return new DetailAction($"{tab}…", () => { }, DetailActionKind.Navigate) { Next = next };
	}

	public static DetailAction OpenAsync(string tab, Func<Task<DetailView>> next)
	{
		return new DetailAction($"{tab}…", () => { }, DetailActionKind.Navigate) { NextAsync = next };
	}
}
