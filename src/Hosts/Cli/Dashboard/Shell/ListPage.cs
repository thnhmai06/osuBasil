using Basil.Host.Cli.Dashboard.Shell.Toasts;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
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
	private readonly Func<T, DetailView> _detailOf;
	private readonly DetailPanel _detail;
	private readonly Dialogs _dialogs;
	private readonly ToastTray _toasts;
	private readonly Glyphs _glyphs;
	private readonly string _help;
	private readonly string _title;
	private readonly GroupedList<T> _list;
	private readonly ILogger _logger;
	private readonly bool _noColor;
	private readonly Action<Action> _post;
	private readonly State<string> _query = new(string.Empty);
	private readonly IListSource<T> _source;
	private readonly State<string> _footer = new(string.Empty);
	private CancellationTokenSource _load = new();
	private string _loadedQuery = string.Empty;
	private long _changedAt;
	private string _seenQuery = string.Empty;
	private int _pages = 1;
	private int _version;
	private bool _more;
	private bool _loading;
	private string? _pendingSelect;
	private bool _focusList;
	private TextBox? _search;
	private TerminalApp? _app;

	/// <summary>Creates the page.</summary>
	public ListPage(string title, string help, IReadOnlyList<Column<T>> columns, Func<T, string> key, IListSource<T> source,
		Func<T, DetailView> detail, Glyphs glyphs, ShellKit kit, bool noColor, TimeProvider clock,
		Action<Action> post, ILogger logger)
	{
		_title = title;
		_help = help;
		_source = source;
		_detailOf = detail;
		_dialogs = kit.Dialogs;
		_toasts = kit.Toasts;
		_detail = new DetailPanel(title, glyphs, new Palette(noColor), kit.Dialogs, kit.Toasts, post);
		_detail.Closed += () => _app?.Focus(_list);
		_glyphs = glyphs;
		_noColor = noColor;
		_clock = clock;
		_post = post;
		_logger = logger;
		_list = new GroupedList<T>(columns, key, glyphs, noColor, clock) { ActivateOnClick = true };
		_list.Activated += _ => Activate(columns);
		_list.NearEnd += LoadMore;
		_list.KeyHook = OnListKey;
		_list.Moved += () =>
		{
			if (_detail.IsOpen && _list.HasSelection) _detail.Show(_detailOf(_list.Selected!));
		};
		_search = new TextBox().Text(_query).Placeholder("search (? for help)");
		_search.KeyDown((_, e) => OnSearchKey(e));
		var searchRow = new HStack(new TextBlock($"{(glyphs.Ascii ? "?" : "⌕")} "), _search.Stretch());
		var body = new VStack(searchRow, new ScrollViewer(_list).Stretch()).Spacing(0);
		var frame = new Group(title).Style(GroupStyle.Rounded).Padding(new Thickness(1, 0, 1, 0)).Content(body)
			.BottomLeftText(() => _footer.Value).Stretch();
		// The detail frame stands under the page, across its whole width, directly above the status bar.
		View = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star() })
			.Rows(new RowDefinition { Height = GridLength.Star() }, new RowDefinition { Height = GridLength.Auto })
			.Cell(frame, 0, 0)
			.Cell(_detail.View, 1, 0);
		foreach (var gesture in Dialogs.BackGestures)
			View.AddCommand(new Command
			{
				Id = $"{title}.back.{gesture}", LabelMarkup = "Back", Gesture = gesture, CanExecute = _ => CanBack,
				Execute = _ => Back()
			});
	}

	/// <summary>Gets the page to put in a tab.</summary>
	public Visual View { get; }

	/// <summary>Connects the page to the running application.</summary>
	public void Attach(TerminalApp app)
	{
		_app = app;
		_detail.Attach(app);
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
		if (_app is { } host && View.Bounds.Width > 0) _detail.Fit(host.Root.Bounds.Width, host.Root.Bounds.Height);
		if (_focusList && _app is not null && View.Bounds.Width > 0)
		{
			_focusList = false;
			_app.Focus(_list);
		}

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

	/// <summary>Opens the help of the search box in a dialog.</summary>
	public void ShowHelp()
	{
		_dialogs.Info($"{_title}: search", _help.Split('\n'));
	}

	/// <summary>Focuses the search box.</summary>
	public void FocusSearch()
	{
		if (_search is not null) _app?.Focus(_search);
	}

	/// <summary>Moves the focus to the list as soon as the tab is on screen.</summary>
	public void FocusList()
	{
		_focusList = true;
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
			section.HasMore ? $"{section.Items.Count}+" : section.Items.Count.ToString(), section.Items))],
			_loadedQuery.Trim().Length > 0);
		if (_pendingSelect is not null && _list.Select(_pendingSelect)) _pendingSelect = null;
		var total = data.Sum(section => section.Items.Count);
		var (first, last) = _list.VisibleItems();
		_footer.Value = total == 0 ? "no items" : $"show {first}–{last} in {total}{(_more ? "+" : string.Empty)}";
		if (_detail.IsOpen && _list.HasSelection) _detail.Update(_detailOf(_list.Selected!));
		else if (_detail.IsOpen) _detail.Close();
	}

	private bool OnListKey(KeyEventArgs e)
	{
		if (e.Char == '/' || e.Char == TerminalChar.CtrlF)
		{
			FocusSearch();
			return true;
		}

		if (e.Char == '?')
		{
			ShowHelp();
			return true;
		}

		if (e.Key == TerminalKey.Up && e.Modifiers == TerminalModifiers.None && _list.AtFirst)
		{
			FocusSearch();
			return true;
		}

		if (!_detail.IsOpen) return false;

		if (e.Key == TerminalKey.Enter && _list.HasSelection || e.Char == 'x')
		{
			_detail.Close();
			return true;
		}

		return false;
	}

	private void OnSearchKey(KeyEventArgs e)
	{
		if (e.Key == TerminalKey.Down)
		{
			LeaveSearch();
		}
		else if (e.Key == TerminalKey.Enter && _query.Value.Trim() == "?")
		{
			_query.Value = string.Empty;
			ShowHelp();
		}
		else
		{
			return;
		}

		e.Handled = true;
	}

	/// <summary>Esc and Alt+Left: goes back in the detail or closes it, else clears the search, else leaves the search box.</summary>
	private void Back()
	{
		if (_detail.IsOpen) _detail.Back();
		else if (_query.Value.Length > 0) _query.Value = string.Empty;
		else LeaveSearch();
	}

	private bool CanBack => _detail.IsOpen || _query.Value.Length > 0 || ReferenceEquals(_app?.FocusedElement, _search);

	private void LeaveSearch()
	{
		_list.SelectFirst();
		_app?.Focus(_list);
	}

	// A click on a cell that links to another item (a player's name) shows that item; any other opens the row's own detail.
	private void Activate(IReadOnlyList<Column<T>> columns)
	{
		if (!_list.HasSelection)
		{
			_detail.Close();
			return;
		}

		try
		{
			var at = _list.ActivatedColumn;
			var link = at >= 0 && at < columns.Count ? columns[at].Link?.Invoke(_list.Selected!) : null;
			_detail.Show(link ?? _detailOf(_list.Selected!));
		}
		catch (Exception exception)
		{
			_toasts.Show(exception.Message, ToastKind.Error);
		}
	}
}
