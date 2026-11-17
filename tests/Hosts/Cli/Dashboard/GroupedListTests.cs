using Basil.Host.Cli.Dashboard.Shell;

namespace Basil.Host.Cli.Tests.Dashboard;

/// <summary>The selection of a table: it starts on the first item, below the selectable title of its group, and can return to it.</summary>
[Collection(HostedTerminalCollection.Name)]
public sealed class GroupedListTests
{
	private static GroupedList<string> Create()
	{
		var list = new GroupedList<string>([new Column<string>("X", 0, s => s)], s => s, new Glyphs(false), false,
			new Microsoft.Extensions.Time.Testing.FakeTimeProvider());
		list.SetSections([new Section<string>("All", "3", ["a", "b", "c"])]);
		return list;
	}

	[Fact]
	public void NewList_SelectsTheFirstItem_NotTheTitleAboveIt()
	{
		var list = Create();

		Assert.False(list.AtFirst);
		Assert.Equal("a", list.Selected);
	}

	[Fact]
	public void Select_AnotherItem_IsNotOnTheFirstItem()
	{
		var list = Create();

		list.Select("b");

		Assert.False(list.AtFirst);
	}

	[Fact]
	public void SelectFirst_ReturnsToTheFirstItemAndTellsSo()
	{
		var list = Create();
		list.Select("c");
		var moved = 0;
		list.Moved += () => moved++;

		list.SelectFirst();

		Assert.Equal("a", list.Selected);
		Assert.Equal(1, moved);
	}

	[Fact]
	public void EmptyList_IsOnTheFirstItem()
	{
		var list = Create();
		list.SetSections([new Section<string>("All", "0", [])]);

		Assert.True(list.AtFirst);
	}
}