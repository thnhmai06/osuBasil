using System.Reflection;
using Basil.Host.Cli.Dashboard.Shell;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Host.Cli.Tests.Dashboard;

/// <summary>The groups of a table during a search: those without a match are hidden, those with one are open, and the folds come back.</summary>
[Collection(HostedTerminalCollection.Name)]
public sealed class GroupFoldTests
{
	private static GroupedList<string> Create()
	{
		return new GroupedList<string>([new Column<string>("X", 0, s => s)], s => s, new Glyphs(false), false, new FakeTimeProvider());
	}

	private static Section<string> Online(params string[] items)
	{
		return new Section<string>("Online", items.Length.ToString(), items);
	}

	private static Section<string> Offline(params string[] items)
	{
		return new Section<string>("Offline", items.Length.ToString(), items);
	}

	// The title of the first group is row 0; this is what a click or Left on it does.
	private static void FoldFirst(GroupedList<string> list)
	{
		typeof(GroupedList<string>).GetMethod("Fold", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, [0, false]);
	}

	private static int Rows(GroupedList<string> list)
	{
		return ((System.Collections.ICollection)typeof(GroupedList<string>)
			.GetField("_rows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(list)!).Count;
	}

	[Fact]
	public void FoldedGroup_HidesItsItems_ButKeepsItsTitle()
	{
		var list = Create();
		list.SetSections([Online("a", "b"), Offline("c")]);

		FoldFirst(list);

		Assert.Equal(1, list.ItemCount);
		Assert.Equal(3, Rows(list));
	}

	[Fact]
	public void Search_HidesGroupsWithoutMatches()
	{
		var list = Create();

		list.SetSections([Online("a"), Offline()], true);

		Assert.Equal(2, Rows(list));
	}

	[Fact]
	public void Search_OpensAGroupTheOperatorFolded()
	{
		var list = Create();
		list.SetSections([Online("a", "b"), Offline("c")]);
		FoldFirst(list);

		list.SetSections([Online("a"), Offline()], true);

		Assert.Equal(1, list.ItemCount);
	}

	[Fact]
	public void ClearingTheSearch_RestoresTheFoldsOfBefore()
	{
		var list = Create();
		list.SetSections([Online("a", "b"), Offline("c")]);
		FoldFirst(list);
		list.SetSections([Online("a"), Offline()], true);

		list.SetSections([Online("a", "b"), Offline("c")]);

		Assert.Equal(1, list.ItemCount);
		Assert.Equal(3, Rows(list));
	}
}
