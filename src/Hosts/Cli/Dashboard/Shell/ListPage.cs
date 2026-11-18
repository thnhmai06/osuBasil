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

internal sealed record SectionData<T>(string Title, IReadOnlyList<T> Items, bool HasMore);

internal interface IListSource<T>
{
	Task<IReadOnlyList<SectionData<T>>> LoadAsync(string query, int limit, CancellationToken ct);
}

internal sealed class ListPage<T>
{
	public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

	private const int PageSize = 100;
	private const int CompactWidth = 100;
	private readonly TimeProvider _clock;
	private readonly Func<T, DetailView> _detailOf;
	private readonly Func<T, string> _keyOf;
	private string? _shownKey;
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
	private bool _stripSlash;
	private readonly SearchBox _searchBox;
	private TextBox? _search;
	private TerminalApp? _app;

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
		_detail = new DetailPanel(title, glyphs, new Palette(noColor), kit.Dialogs, kit.Toasts, post, kit.Changes);
		_keyOf = key;
		kit.Changes.Occurred += OnChanged;
		_detail.Closed += () => _app?.Focus(_list);
		_glyphs = glyphs;
		_noColor = noColor;
		_clock = clock;
		_post = post;
		_logger = logger;
		_list = new GroupedList<T>(columns, key, glyphs, noColor, clock);
		_list.Activated += _ => Activate(columns);
		_list.NearEnd += LoadMore;
		_list.KeyHook = OnListKey;
		_list.Moved += () =>
		{
			if (!_detail.IsOpen || !_list.HasSelection) return;

			_shownKey = _keyOf(_list.Selected!);
			_detail.Show(_detailOf(_list.Selected!));
		};
		_searchBox = new SearchBox(_query, "search (? for help)", glyphs);
		_search = _searchBox.Box;
		_search.KeyDown((_, e) => OnSearchKey(e));
		var body = new VStack(_searchBox.Row, new ScrollViewer(_list).Stretch()).Spacing(0);
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

	public Visual View { get; }

	public void Attach(TerminalApp app)
	{
		_app = app;
		_detail.Attach(app);
	}

	public void Refresh()
	{
		_pages = 1;
		Start();
	}

	public void Tick()
	{
		_searchBox.Tick();
		if (_app is { } host && View.Bounds.Width > 0) _detail.Fit(host.Root.Bounds.Width, host.Root.Bounds.Height);
		if (_focusList && _app is not null && View.Bounds.Width > 0)
		{
			_focusList = false;
			_app.Focus(_list);
		}

		if (_stripSlash)
		{
			_stripSlash = false;
			if (_query.Value.StartsWith('/')) _query.Value = _query.Value[1..];
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

	public void ShowHelp()
	{
		_dialogs.Info($"{_title}: search", _help.Split('\n'));
	}

	public void FocusSearch()
	{
		if (_search is not null) _app?.Focus(_search);
	}

	public void FocusList()
	{
		_focusList = true;
	}

	public void Select(string id)
	{
		_pendingSelect = id;
		if (_list.Select(id)) _pendingSelect = null;
	}

	// An action changed the server: the list reads again once it has been loaded, which also refreshes the detail.
	private void OnChanged()
	{
		if (_version > 0) Start();
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
		if (!_detail.IsOpen || _shownKey is not { } shown) return;

		// What the detail shows was removed (a closed session, a deleted account): the detail closes with it.
		if (_list.TryFind(shown, out var item)) _detail.Update(_detailOf(item));
		else _detail.Close();
	}

	private bool OnListKey(KeyEventArgs e)
	{
		if (e.Char == '/' || e.Char == TerminalChar.CtrlF)
		{
			// The slash is also delivered as text to the box that takes the focus; Tick removes it.
			_stripSlash = e.Char == '/';
			FocusSearch();
			return true;
		}

		if (e.Char == '?')
		{
			ShowHelp();
			return true;
		}

		if (e is { Key: TerminalKey.Up, Modifiers: TerminalModifiers.None } && _list.AtFirst)
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
			if (link is { } read)
			{
				_shownKey = null;
				_detail.ShowAsync(read.Title, read.Read);
			}
			else
			{
				_shownKey = _keyOf(_list.Selected!);
				_detail.Show(_detailOf(_list.Selected!));
			}
		}
		catch (Exception exception)
		{
			_toasts.Show(exception.Message, ToastKind.Error);
		}
	}
}
