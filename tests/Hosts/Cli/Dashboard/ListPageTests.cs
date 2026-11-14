using Basil.Host.Cli.Dashboard.Shell;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Host.Cli.Tests.Dashboard;

/// <summary>The search of a list page: it waits for the text to stand still, and never shows an answer that was replaced.</summary>
public sealed class ListPageTests
{
	private sealed class Source : IListSource<string>
	{
		public List<string> Queries { get; } = [];

		public Task<IReadOnlyList<SectionData<string>>> LoadAsync(string query, int limit, CancellationToken ct)
		{
			lock (Queries) Queries.Add(query);
			return Task.FromResult<IReadOnlyList<SectionData<string>>>([new SectionData<string>("All", [query], false)]);
		}
	}

	private static (ListPage<string> Page, Source Source, FakeTimeProvider Clock) Create()
	{
		var clock = new FakeTimeProvider();
		var source = new Source();
		var page = new ListPage<string>("T", "help", [new Column<string>("X", 0, s => s)], s => s, source,
			_ => new DetailView([], []), new Glyphs(false), false, clock, action => action(),
			NullLogger.Instance);
		return (page, source, clock);
	}

	[Fact]
	public async Task Search_WaitsUntilTheTextStandsStill()
	{
		var (page, source, clock) = Create();
		var field = typeof(ListPage<string>).GetField("_query", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
		var query = (XenoAtom.Terminal.UI.State<string>)field.GetValue(page)!;

		query.Value = "bob";
		page.Tick();
		clock.Advance(TimeSpan.FromMilliseconds(499));
		page.Tick();
		await Task.Delay(50, TestContext.Current.CancellationToken);
		Assert.Empty(source.Queries);

		clock.Advance(TimeSpan.FromMilliseconds(1));
		page.Tick();
		await Task.Delay(50, TestContext.Current.CancellationToken);
		Assert.Equal(["bob"], source.Queries);
	}

	[Fact]
	public void Back_WithSearchText_ClearsTheText()
	{
		var (page, _, _) = Create();
		var query = Query(page);
		query.Value = "bob";

		Back(page);

		Assert.Equal(string.Empty, query.Value);
	}

	[Fact]
	public void Back_WithEmptySearch_LeavesTheSearchEmpty()
	{
		var (page, _, _) = Create();
		var query = Query(page);

		Back(page);

		Assert.Equal(string.Empty, query.Value);
	}

	private static XenoAtom.Terminal.UI.State<string> Query(ListPage<string> page)
	{
		var field = typeof(ListPage<string>).GetField("_query", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
		return (XenoAtom.Terminal.UI.State<string>)field.GetValue(page)!;
	}

	private static void Back(ListPage<string> page)
	{
		typeof(ListPage<string>).GetMethod("Back", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
			.Invoke(page, null);
	}
}
