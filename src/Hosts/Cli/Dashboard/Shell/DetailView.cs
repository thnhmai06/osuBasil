using XenoAtom.Terminal.UI;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>What the detail frame shows for an item.</summary>
/// <param name="Title">The name of the item, which is its step in the breadcrumb on the frame.</param>
/// <param name="Fields">The label and value pairs, shown as two columns.</param>
/// <param name="Actions">The actions; empty when the console is view only.</param>
/// <param name="Body">
///     Builds what stands in place of the fields, such as a table; it gets a callback that shows another view as the
///     next step of the frame. It runs on the thread of the interface.
/// </param>
internal sealed record DetailView(string Title, IReadOnlyList<(string Label, string Value, Ink Kind)> Fields,
	IReadOnlyList<DetailAction> Actions, Func<Action<DetailView>, Visual>? Body = null)
{
	/// <summary>Gets how to read the item again, so that the frame can show what an action changed; <see langword="null" /> when it has nothing to read.</summary>
	public Func<Task<DetailView>>? Reload { get; init; }
}
