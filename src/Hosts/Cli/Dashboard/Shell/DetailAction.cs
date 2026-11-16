namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>One action of the detail frame.</summary>
/// <param name="Label">The button text.</param>
/// <param name="Run">Does the action; it runs on the thread of the interface.</param>
/// <param name="Kind">What the action does, which decides where its button stands.</param>
internal sealed record DetailAction(string Label, Action Run, DetailActionKind Kind = DetailActionKind.Normal)
{
	/// <summary>Gets what the frame shows in place of the current detail when this action is a step into another tab.</summary>
	public Func<DetailView>? Next { get; init; }

	/// <summary>Makes an action that opens the detail or the list of another tab inside the frame.</summary>
	/// <param name="tab">The name of the other tab; the button reads <c>tab…</c>.</param>
	/// <param name="next">Builds what the frame shows next.</param>
	public static DetailAction Open(string tab, Func<DetailView> next)
	{
		return new DetailAction($"{tab}…", () => { }, DetailActionKind.Navigate) { Next = next };
	}
}
