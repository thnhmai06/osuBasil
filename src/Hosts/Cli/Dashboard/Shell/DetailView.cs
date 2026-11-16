namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>What the detail frame shows for an item.</summary>
/// <param name="Title">The name of the item, which is its step in the breadcrumb on the frame.</param>
/// <param name="Fields">The label and value pairs, shown as two columns.</param>
/// <param name="Actions">The actions; empty when the console is view only.</param>
internal sealed record DetailView(string Title, IReadOnlyList<(string Label, string Value, Ink Kind)> Fields,
	IReadOnlyList<DetailAction> Actions);
