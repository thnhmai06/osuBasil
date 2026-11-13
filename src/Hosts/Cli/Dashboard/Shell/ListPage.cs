using Microsoft.Extensions.Logging;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>The items of one group, as a source returns them.</summary>
/// <param name="Title">The name of the group.</param>
/// <param name="Items">The items loaded so far.</param>
/// <param name="HasMore">Whether the source holds more items for the group than <paramref name="Items" />.</param>
internal sealed record SectionData<T>(string Title, IReadOnlyList<T> Items, bool HasMore);

/// <summary>Where a list page gets its items.</summary>
internal interface IListSource<T>
{
	/// <summary>Loads the groups for a search text.</summary>
	/// <param name="query">The text of the search box.</param>
	/// <param name="limit">How many items a group that lives in the database may return.</param>
	/// <param name="ct">Cancels the load when the operator searches again.</param>
	Task<IReadOnlyList<SectionData<T>>> LoadAsync(string query, int limit, CancellationToken ct);
}

/// <summary>One action of the detail frame.</summary>
/// <param name="Label">The button text.</param>
/// <param name="Run">Does the action; it is a console command, so it runs through the command runner.</param>
/// <param name="Destructive">Whether it ends something, which shows in the error colour.</param>
internal sealed record DetailAction(string Label, Func<Task> Run, bool Destructive = false);

/// <summary>What the detail frame shows for an item.</summary>
/// <param name="Fields">The label and value pairs, shown left to right in two columns.</param>
/// <param name="Actions">The actions on the right; empty when the console is view only.</param>
internal sealed record DetailView(IReadOnlyList<(string Label, string Value, Ink Kind)> Fields,
	IReadOnlyList<DetailAction> Actions);

/// <summary>
///     A page that lists the items of a table with a search box on top, groups that keep their title, a footer with the
///     range in view, and a detail frame under the list; every table tab builds on it.
/// </summary>
internal sealed class ListPage<T>
{
	/// <summary>How long the search text must stand still before the database is asked.</summary>
	public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

	private const int PageSize = 100;
	private const int CompactWidth = 100;
	private readonly TimeProvider _clock;
	private readonly Func<T, DetailView> _detail;
	private readonly Group _detailBox = new();
	private readonly Glyphs _glyphs;
	private readonly GroupedList<T> _list;
	private readonly ILogger _logger;
	private readonly bool _noColor;
	private readonly Action<Action> _post;
	private readonly State<string> _query = new(string.Empty);
	private readonly IListSource<T> _source;
	private readonly State<string> _footer = new(string.Empty);
	private readonly State<bool> _detailOpen = new(false);
	private CancellationTokenSource _load = new();
	private string _loadedQuery = string.Empty;
	private long _changedAt;
	private string _seenQuery = string.Empty;
	private int _pages = 1;
	private int _version;
	private bool _more;
	private bool _loading;
	private string? _pendingSelect;
	private TextBox? _search;
	private TerminalApp? _app;
	private bool _compact;
	private int _cap = int.MaxValue;
	private DetailView? _view;

	/// <summary>Creates the page.</summary>
	public ListPage(string title, IReadOnlyList<Column<T>> columns, Func<T, string> key, IListSource<T> source,
		Func<T, DetailView> detail, Glyphs glyphs, bool noColor, TimeProvider clock, Action<Action> post,
		ILogger logger)
	{
		_source = source;
		_detail = detail;
		_glyphs = glyphs;
		_noColor = noColor;
		_clock = clock;
		_post = post;
		_logger = logger;
		_list = new GroupedList<T>(columns, key, glyphs, noColor);
		_list.Activated += _ => ToggleDetail(true);
		_list.NearEnd += LoadMore;
		_list.KeyHook = OnListKey;
		_search = new TextBox().Text(_query).Placeholder("search");
		var searchRow = new HStack(new TextBlock($"{(glyphs.Ascii ? "?" : "⌕")} "), _search.Stretch());
		var body = new VStack(searchRow, new ScrollViewer(_list).Stretch(), _detailBox.Style(glyphs.Ascii ? GroupStyle.Ascii : GroupStyle.Single).Stretch().IsVisible(_detailOpen)).Spacing(0);
		View = new Group(title).Style(GroupStyle.Rounded).Padding(new Thickness(1, 0, 1, 0)).Content(body)
			.BottomLeftText(() => _footer.Value).Stretch();
	}

	/// <summary>Gets the page to put in a tab.</summary>
	public Visual View { get; }

	/// <summary>Connects the page to the running application.</summary>
	public void Attach(TerminalApp app)
	{
		_app = app;
	}

	/// <summary>Loads the first page; call it when the tab opens and when the operator refreshes.</summary>
	public void Refresh()
	{
		_pages = 1;
		Start();
	}

	/// <summary>Lets time pass: starts the search once the text stood still long enough.</summary>
	public void Tick()
	{
		FitDetail();
		var text = _query.Value;
		var now = _clock.GetTimestamp();
		if (text != _seenQuery)
		{
			_seenQuery = text;
			_changedAt = now;
			return;
		}

		if (text != _loadedQuery && _clock.GetElapsedTime(_changedAt, now) >= Debounce)
		{
			_pages = 1;
			Start();
		}
	}

	/// <summary>Focuses the search box.</summary>
	public void FocusSearch()
	{
		if (_search is not null) _app?.Focus(_search);
	}

	/// <summary>Selects an item by its id, loading it first when needed.</summary>
	public void Select(string id)
	{
		_pendingSelect = id;
		if (_list.Select(id)) _pendingSelect = null;
	}

	private void LoadMore()
	{
		if (!_more || _loading) return;

		_pages++;
		Start();
	}

	private void Start()
	{
		var query = _query.Value;
		var version = Interlocked.Increment(ref _version);
		_load.Cancel();
		_load = new CancellationTokenSource();
		var token = _load.Token;
		_loadedQuery = query;
		_seenQuery = query;
		_loading = true;
		var limit = _pages * PageSize;
		_ = Task.Run(async () =>
		{
			try
			{
				var data = await _source.LoadAsync(query, limit, token);
				_post(() =>
				{
					if (version != _version) return;

					Show(data);
				});
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "A list could not be loaded");
				_post(() => _loading = false);
			}
		}, token);
	}

	private void Show(IReadOnlyList<SectionData<T>> data)
	{
		_loading = false;
		_more = data.Any(section => section.HasMore);
		_list.SetSections([.. data.Select(section => new Section<T>(section.Title,
			section.HasMore ? $"{section.Items.Count}+" : section.Items.Count.ToString(), section.Items))]);
		if (_pendingSelect is not null && _list.Select(_pendingSelect)) _pendingSelect = null;
		var total = data.Sum(section => section.Items.Count);
		var (first, last) = _list.VisibleItems();
		_footer.Value = total == 0 ? "no items" : $"show {first}–{last} in {total}{(_more ? "+" : string.Empty)}";
		if (_detailOpen.Value && _list.HasSelection) FillDetail(_list.Selected!);
		else if (_detailOpen.Value) ToggleDetail(false);
	}

	private bool OnListKey(KeyEventArgs e)
	{
		if (e.Char == '/' || (e.Char == 'f' && e.Modifiers.HasFlag(TerminalModifiers.Ctrl)))
		{
			FocusSearch();
			return true;
		}

		if (!_detailOpen.Value) return false;

		if (e.Key is TerminalKey.Escape or TerminalKey.Enter || e.Char == 'x')
		{
			ToggleDetail(false);
			return true;
		}

		return false;
	}

	private void ToggleDetail(bool open)
	{
		_detailOpen.Value = open && _list.HasSelection;
		if (_detailOpen.Value) FillDetail(_list.Selected!);
	}

	/// <summary>Keeps the detail frame to a third of the page when the page is short, and folds its actions below 100 columns.</summary>
	private void FitDetail()
	{
		var (width, height) = (View.Bounds.Width, View.Bounds.Height);
		if (width <= 0 || height <= 0) return;

		var cap = height < Layout.WideSize.Height ? Math.Max(height / 3, 3) : int.MaxValue;
		if (cap != _cap)
		{
			_cap = cap;
			_detailBox.MaxHeight(cap);
		}

		var compact = width < CompactWidth;
		if (compact == _compact) return;

		_compact = compact;
		if (_detailOpen.Value && _view is not null) Fill(_view);
	}

	private void FillDetail(T item)
	{
		Fill(_view = _detail(item));
	}

	private void Fill(DetailView view)
	{
		var fields = new VStack().Spacing(0);
		foreach (var (label, value, _) in view.Fields)
			fields.Add(new TextBlock($"{label,-12} {Palette.Plain(value)}"));
		var actions = new VStack().Spacing(0);
		if (_compact && view.Actions.Count > 0)
			actions.Add(new Button("[Actions]").Click(() => ShowActions(view.Actions)));
		else
			foreach (var action in view.Actions)
				actions.Add(new Button(action.Label).Click(() => _ = Task.Run(action.Run)));
		var top = new HStack(fields.Stretch(), actions);
		_detailBox.Content(new VStack(top, new TextBlock("Esc/Enter/x · Close")));
	}

	private static void ShowActions(IReadOnlyList<DetailAction> actions)
	{
		var dialog = new Dialog().Title("Actions");
		var rows = new VStack().Spacing(0);
		foreach (var action in actions)
			rows.Add(new Button(action.Label).Click(() =>
			{
				dialog.Close();
				_ = Task.Run(action.Run);
			}));
		rows.Add(new Button("Cancel").Click(dialog.Close));
		dialog.Content(Dialogs.EscapeCloses(dialog, rows));
		dialog.Show();
	}
}
