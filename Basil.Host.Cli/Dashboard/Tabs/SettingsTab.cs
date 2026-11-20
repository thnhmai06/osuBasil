using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Application.Servers.Messages.Content;
using Basil.Application.Users.Registry;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard.Shell;
using Basil.Host.Cli.Dashboard.Shell.Toasts;
using Basil.Host.Shared.Json;
using Basil.Host.Shared.Views;
using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;
using Group = XenoAtom.Terminal.UI.Controls.Group;

namespace Basil.Host.Cli.Dashboard.Tabs;

internal sealed class SettingsTab : ITabView
{
	private const string StripHelp = "Down or Enter goes to the settings of the tab; Left and Right change the tab.";
	private const string NotNumber = "Enter a number.";
	private const int NestedIndent = 2;
	private readonly HashSet<string> _closed = [];
	private readonly ConsoleTab _console;

	private readonly Group _content = new();
	private readonly State<string> _description = new(string.Empty);
	private readonly List<Field> _fields = [];
	private readonly Glyphs _glyphs;
	private readonly ShellKit _kit;
	private readonly ILogger _logger;
	private readonly List<Visual> _nav = [];
	private readonly CliOptions _options;
	private readonly IReadOnlyList<(string Name, string[] Categories)> _pages = SettingsPages.All;
	private readonly Palette _palette;
	private readonly State<string> _path = new(string.Empty);
	private readonly Action<Action> _post;
	private readonly State<string> _query = new(string.Empty);
	private readonly ScrollViewer _scroll = new() { HorizontalScrollEnabled = false };
	private readonly TextBox _search;
	private readonly HashSet<string> _searchClosed = [];
	private readonly List<Section> _sections = [];
	private readonly State<int> _selected = new(0);
	private readonly IServiceProvider _services;
	private readonly TabSpread _spread;
	private readonly TabControl _strip;
	private readonly State<string> _title = new(string.Empty);
	private TerminalApp? _app;
	private string _applied = string.Empty;
	private int _labelWidth;
	private int _loaded = -1;
	private bool _reload;
	private int _saving;
	private int _version;

	public SettingsTab(IServiceProvider services, ConsoleTab console, CliOptions options, Glyphs glyphs, ShellKit kit,
		TimeProvider clock, Action<Action> post, ILogger logger)
	{
		_kit = kit;
		_services = services;
		_console = console;
		_options = options;
		_glyphs = glyphs;
		_post = post;
		_logger = logger;
		_palette = new Palette(options.NoColor);
		var stripStyle = options.Ascii ? TabControlStyle.Ascii : TabControlStyle.NoBorder;
		_strip = new TabControl(_pages.Select(page => new TabPage(page.Name, new TextBlock(string.Empty))).ToArray())
			.Style(stripStyle).SelectedIndex(_selected);
		_spread = new TabSpread(_strip, stripStyle, _pages.Select(page => page.Name));
		_strip.AddCommand(new Command
		{
			Id = "settings.strip.down", LabelMarkup = "Search", Gesture = new KeyGesture(TerminalKey.Down),
			ConsumesGestureWhenUnavailable = false, Execute = _ => _app?.Focus(_search)
		});
		_strip.AddCommand(new Command
		{
			Id = "settings.strip.enter", LabelMarkup = "Search", Gesture = new KeyGesture(TerminalKey.Enter),
			ConsumesGestureWhenUnavailable = false, Execute = _ => _app?.Focus(_search)
		});
		var description = new Group().Style(GroupStyle.Rounded).Padding(new Thickness(1, 0, 1, 0))
			.Content(new ScrollViewer(new VStack(new TextBlock(() => _title.Value).Wrap(true),
				new Markup(() => _path.Value),
				new TextBlock(() => _description.Value).Wrap(true))))
			.Stretch();
		_search = new TextBox().Text(_query).Placeholder("search");
		_search.KeyDown((_, e) =>
		{
			if (e.Key == TerminalKey.Down) FocusNav(0);
			else if (e.Key == TerminalKey.Up) _app?.Focus(_strip);
			else return;

			e.Handled = true;
		});
		var searchRow = new HStack(new TextBlock($"{(glyphs.Ascii ? "?" : "⌕")} "), _search.Stretch()).MaxHeight(1);
		_content.Style(GroupStyle.Rounded).Padding(new Thickness(1, 0, 1, 0))
			.Content(new Grid().Columns(new ColumnDefinition { Width = GridLength.Star() })
				.Rows(new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star() })
				.Cell(searchRow, 0, 0).Cell(WheelScroll.Accelerate(_scroll, clock).Stretch(), 1, 0)).Stretch();
		foreach (var gesture in Dialogs.BackGestures)
			_content.AddCommand(new Command
			{
				Id = $"settings.back.{gesture}", LabelMarkup = "Back", Gesture = gesture,
				CanExecute = _ => ReferenceEquals(_app?.FocusedElement, _search),
				ConsumesGestureWhenUnavailable = false,
				Execute = _ =>
				{
					if (_query.Value.Length > 0) _query.Value = string.Empty;
					else _app?.Focus(_strip);
				}
			});
		var body = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star(2) },
				new ColumnDefinition { Width = GridLength.Star() })
			.Rows(new RowDefinition { Height = GridLength.Star() })
			.Cell(_content, 0, 0)
			.Cell(description, 0, 1);
		View = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star() })
			.Rows(new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star() })
			.Cell(_strip.MaxHeight(1), 0, 0)
			.Cell(body, 1, 0);
		ShowStripHelp();
	}

	private (string Name, string[] Categories) Page => _pages[Math.Clamp(_selected.Value, 0, _pages.Count - 1)];

	public Visual View { get; }

	public void Attach(TerminalApp app)
	{
		_app = app;
	}

	public void Activate()
	{
		// An edit left unsaved when the operator switched tabs is saved before the form is read again.
		foreach (var field in _fields) Save(field);
		Load();
		_app?.Focus(_strip);
		ShowStripHelp();
	}

	public void Tick()
	{
		_spread.Fit(View.Bounds.Width);
		if (_selected.Value != _loaded) Load();

		SizeLabels();
		if (_query.Value != _applied)
		{
			_searchClosed.Clear();
			_scroll.VerticalOffset = 0;
			Rebuild();
		}

		var anyFocused = false;
		foreach (var field in _fields)
		{
			var focused = field.Control.HasFocus;
			if (focused)
			{
				anyFocused = true;
				_title.Value = field.Label;
				_path.Value = _palette.Wrap(Ink.Muted, $"{field.Category}.{field.Path}");
				_description.Value = field.Description;
			}
			else if (field.Focused)
			{
				Save(field);
			}

			field.Focused = focused;
		}

		var title = _sections.Find(section => section.Title.HasFocus);
		if (title is not null)
		{
			anyFocused = true;
			_title.Value = title.Display;
			_path.Value = string.Empty;
			_description.Value = "Enter, Left and Right open and fold the group.";
		}

		if (!anyFocused && (_strip.HasFocus || ReferenceEquals(_app?.FocusedElement, _search))) ShowStripHelp();
	}

	public void Refresh()
	{
		Load();
	}

	private void ShowStripHelp()
	{
		_title.Value = Page.Name;
		_path.Value = string.Empty;
		_description.Value = Page.Name == SettingsPages.Configuration
			? "Read from the configuration of the server; edit its files and restart."
			: StripHelp;
	}

	private void FocusNav(int index)
	{
		if (index < 0)
		{
			_app?.Focus(_search);
			return;
		}

		if (index >= _nav.Count) return;

		if (_nav[index] is PromptEditor editor) editor.CaretIndex = (editor.Text ?? string.Empty).Length;
		_app?.Focus(_nav[index]);
	}

	private void Step(Visual from, int delta)
	{
		var index = _nav.IndexOf(from);
		if (index >= 0) FocusNav(Math.Clamp(index + delta, -1, _nav.Count - 1));
	}

	private void Load()
	{
		if (_saving > 0)
		{
			_reload = true;
			return;
		}

		_loaded = _selected.Value;
		_query.Value = string.Empty;
		var page = Page;
		var version = Interlocked.Increment(ref _version);
		if (page.Name == SettingsPages.Configuration)
		{
			var environment = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
				.ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString() ?? string.Empty);
			Show(version, page.Name, ConfigurationRows.Of(_services.GetRequiredService<IConfiguration>(), environment));
			return;
		}

		_ = Task.Run(async () =>
		{
			try
			{
				var registry = _services.GetRequiredService<UserRegistry>();
				var actor = await registry.OperateAsync(CancellationToken.None);
				var (_, settings) = await _services.GetRequiredService<ISender>().Send(new GetSettings(actor));
				if (settings is null) return;

				var rows = new List<SettingsRow>();
				foreach (var category in page.Categories)
				{
					var flat = new List<(string Path, string Value, SettingsFieldKind Kind)>();
					Flatten(flat, string.Empty,
						JsonSerializer.SerializeToNode(SettingsCategories.Get(category, settings), ApiJson.Options));
					rows.AddRange(flat.Select(row =>
					{
						var dot = row.Path.IndexOf('.');
						return new SettingsRow(category, dot < 0 ? null : row.Path[..dot], row.Path,
							SettingsLabels.Of($"{category}.{row.Path}") ?? row.Path, row.Value, row.Kind);
					}));
				}

				Show(version, page.Name, rows);
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "The settings could not be loaded");
			}
		});
	}

	private void Show(int version, string page, IReadOnlyList<SettingsRow> rows)
	{
		_post(() =>
		{
			if (version != _version) return;

			try
			{
				_fields.Clear();
				_sections.Clear();
				var readOnly = page == SettingsPages.Configuration;
				foreach (var row in rows)
				{
					var section = SectionOf(row);
					var value = row.Value;
					Visual control;
					if (readOnly)
						control = new TextBlock(Palette.Plain(value)).Trimming(TextTrimming.EndEllipsis);
					else if (row.Kind == SettingsFieldKind.Bool)
						control = new Switch().IsOn(value == "true");
					else
						control = new PromptEditor().LineMode(PromptEditorLineMode.SingleLine)
							.PromptMarkup(string.Empty)
							.Text(value);

					var field = new Field(row.Category, row.Path, row.Label,
						SettingsDescriptions.Of($"{row.Category}.{row.Path}") ?? string.Empty, value, row.Kind,
						control);
					if (!readOnly)
					{
						if (control is PromptEditor editor) editor.Accepted(() => Save(field));
						else if (control is Switch toggle) toggle.Toggled(() => Save(field));
						AddKeys(control, $"settings.{row.Category}.{row.Path}");
						AddEscape(field);
						field.Editable = true;
						_fields.Add(field);
					}

					// Every label has the width of the label column, so every row of every group lines up.
					var shown = row.Raw ? row.Label : Relative(row.Label, row.Group ?? row.Category, row.Category);
					// A row of a group inside a group is moved right with its title; its label is as much shorter, so the
					// values still line up in one column.
					field.LabelBox = new TextBlock(Palette.Plain(shown)).Trimming(TextTrimming.EndEllipsis)
						.Margin(new Thickness(Indent(section), 0, 1, 0));
					field.Row = new HStack(field.LabelBox, control.Stretch()).Spacing(0);
					section.Fields.Add(field);
				}

				// The tree in the order it is shown: a category, its own settings, then its groups.
				var list = new VStack().Spacing(0);
				foreach (var section in _sections.Where(s => s.Parent is null).SelectMany(s => s.Subtree()))
				{
					list.Add(section.Title);
					foreach (var field in section.Fields) list.Add(field.Row!);
				}

				_labelWidth = 0;
				_scroll.Content(list);
				_scroll.VerticalOffset = 0;
				Rebuild();
				ShowStripHelp();
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "The settings could not be shown");
			}
		});
	}

	// The group of a row: its category, or a group inside the category.
	private Section SectionOf(SettingsRow row)
	{
		var top = Find(row, null);
		return row.Group is null ? top : Find(row, top);
	}

	private Section Find(SettingsRow row, Section? parent)
	{
		var name = parent is null ? row.Category : row.Group!;
		var key = parent is null ? row.Category : $"{row.Category}.{row.Group}";
		var found = _sections.Find(s => s.Key == key);
		if (found is not null) return found;

		var display = row.Raw ? name : Words.Humanise(name);
		var title = new FoldTitle(display, _glyphs, _options.NoColor, parent is null ? 0 : NestedIndent);
		var section = new Section(key, display, title, parent);
		title.Folding += open => Fold(section, open);
		AddKeys(title, $"settings.{key}");
		parent?.Children.Add(section);
		_sections.Add(section);
		return section;
	}

	// The label column takes a little over half of the room of the fields, whatever the size of the terminal.
	private static int Indent(Section section)
	{
		return section.Parent is null ? 0 : NestedIndent;
	}

	private void SizeLabels()
	{
		var width = Math.Clamp(_scroll.Bounds.Width * 5 / 9, 12, SettingsLabels.MaxLength);
		if (width == _labelWidth) return;

		_labelWidth = width;
		foreach (var section in _sections)
		foreach (var field in section.Fields)
			field.LabelBox!.MinWidth(width - Indent(section)).MaxWidth(width - Indent(section));
	}

	// A label that begins with the name of its own group or category ("Login: attempt window") drops it.
	private static string Relative(string label, string group, string category)
	{
		var colon = label.IndexOf(':');
		if (colon <= 0) return label;

		var head = label[..colon];
		if (!head.Equals(Words.Humanise(group), StringComparison.OrdinalIgnoreCase) &&
		    !head.Equals(Words.Humanise(category), StringComparison.OrdinalIgnoreCase))
			return label;

		var rest = label[(colon + 1)..].TrimStart();
		return rest.Length > 0 ? char.ToUpperInvariant(rest[0]) + rest[1..] : label;
	}

	private void Fold(Section section, bool open)
	{
		var folded = _applied.Trim().Length > 0 ? _searchClosed : _closed;
		if (open) folded.Remove(section.Key);
		else folded.Add(section.Key);
		Rebuild();
	}

	// Shows the groups with what matches the search (all of it when there is no search), opens or folds each one, and
	// lists what the keys move over.
	private void Rebuild()
	{
		_applied = _query.Value;
		var query = _applied.Trim();
		var searching = query.Length > 0;
		var folded = searching ? _searchClosed : _closed;
		_nav.Clear();
		foreach (var section in _sections)
			section.Matching = section.Fields.Where(field => !searching || field.Matches(query)).ToList();
		foreach (var section in _sections.Where(s => s.Parent is null)) Place(section, true, folded);
	}

	private void Place(Section section, bool parentOpen, HashSet<string> folded)
	{
		var open = !folded.Contains(section.Key);
		var total = section.Subtree().Sum(member => member.Matching.Count);
		section.Title.Open = open;
		section.Title.Count = total;
		section.Title.IsVisible = parentOpen && total > 0;
		if (section.Title.IsVisible) _nav.Add(section.Title);
		foreach (var field in section.Fields)
		{
			var shown = parentOpen && open && section.Matching.Contains(field);
			field.Row!.IsVisible = shown;
			if (shown && field.Editable) _nav.Add(field.Control);
		}

		foreach (var child in section.Children) Place(child, parentOpen && open, folded);
	}

	// Up and Down move between the titles and the fields, Up on the first one goes to the search box.
	private void AddKeys(Visual visual, string id)
	{
		visual.AddCommand(new Command
		{
			Id = $"{id}.down", LabelMarkup = "down", Gesture = new KeyGesture(TerminalKey.Down),
			Execute = _ => Step(visual, 1)
		});
		visual.AddCommand(new Command
		{
			Id = $"{id}.up", LabelMarkup = "up", Gesture = new KeyGesture(TerminalKey.Up),
			Execute = _ => Step(visual, -1)
		});
	}

	// Esc and Alt+Left go back to the strip, and Esc also drops an edit that was not saved.
	private void AddEscape(Field field)
	{
		var id = $"settings.{field.Category}.{field.Path}";

		void Add(string name, KeyGesture gesture, Action run)
		{
			field.Control.AddCommand(new Command
			{
				Id = $"{id}.{name}", LabelMarkup = name, Gesture = gesture, Execute = _ => run()
			});
		}

		// The editor takes Esc for itself and reports it as canceled.
		if (field.Control is PromptEditor editor) editor.Canceled(() => Leave(field));
		else Add("escape", new KeyGesture(TerminalKey.Escape), () => Leave(field));
		// A switch only toggles on Enter by itself; Space does the same.
		if (field.Control is Switch toggle)
			Add("toggle", new KeyGesture(' '), () =>
			{
				toggle.IsOn = !toggle.IsOn;
				Save(field);
			});
		Add("back", new KeyGesture(TerminalKey.Left, TerminalModifiers.Alt), () => Leave(field));
	}

	private void Leave(Field field)
	{
		switch (field.Control)
		{
			case PromptEditor editor:
				editor.Text = field.Original;
				break;
			case Switch toggle:
				toggle.IsOn = field.Original == "true";
				break;
		}

		_app?.Focus(_strip);
	}

	private void Save(Field field)
	{
		var text = field.Control switch
		{
			PromptEditor editor => editor.Text ?? string.Empty,
			Switch toggle => toggle.IsOn ? "true" : "false",
			_ => field.Original
		};
		if (text == field.Original) return;

		var json = ToJson(field.Kind, text);
		if (json is null)
		{
			// Leaving the same wrong value again does not repeat the message.
			if (field.Rejected != text) _kit.Toasts.Show(NotNumber, ToastKind.Error);
			field.Rejected = text;
			return;
		}

		field.Rejected = null;

		field.Original = text;
		_saving++;
		_ = _console.RunCommand($"settings {field.Category} set --{field.Path} {Quote(json)}", ShowResult)
			.ContinueWith(_ => _post(() =>
			{
				if (--_saving > 0 || !_reload) return;

				_reload = false;
				Load();
			}), TaskScheduler.Default);
	}

	private static string? ToJson(SettingsFieldKind kind, string text)
	{
		switch (kind)
		{
			case SettingsFieldKind.Bool:
				return text;
			case SettingsFieldKind.Number:
				return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
					? number.ToString(CultureInfo.InvariantCulture)
					: null;
			case SettingsFieldKind.List:
				return new JsonArray([
					.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
						.Select(item => (JsonNode?)JsonValue.Create(item))
				]).ToJsonString();
			case SettingsFieldKind.Optional when text.Length == 0:
				return "null";
			default:
				return JsonValue.Create(text).ToJsonString();
		}
	}

	private static string Quote(string value)
	{
		return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
	}

	// The output of the command goes to the log as well; here a toast tells how it ended.
	private void ShowResult(OutputLine line)
	{
		var kind = line.Kind switch
		{
			OutputKind.Error => ToastKind.Error,
			OutputKind.Ok => ToastKind.Info,
			_ => (ToastKind?)null
		};
		if (kind is { } shown) _kit.Toasts.Show(line.Text.Split('\n')[0].TrimEnd('\r'), shown);
	}

	private static void Flatten(List<(string Path, string Value, SettingsFieldKind Kind)> rows, string prefix,
		JsonNode? node)
	{
		switch (node)
		{
			case JsonObject obj:
				foreach (var (key, value) in obj) Flatten(rows, prefix.Length == 0 ? key : $"{prefix}.{key}", value);
				break;
			case JsonValue value:
				rows.Add((prefix, value.ToString(), value.GetValueKind() switch
				{
					JsonValueKind.True or JsonValueKind.False => SettingsFieldKind.Bool,
					JsonValueKind.Number => SettingsFieldKind.Number,
					_ => SettingsFieldKind.Text
				}));
				break;
			case null when prefix.Length > 0:
				rows.Add((prefix, string.Empty, SettingsFieldKind.Optional));
				break;
			case JsonArray array:
				rows.Add((prefix, string.Join(", ", array.Select(item => item?.ToString())), SettingsFieldKind.List));
				break;
		}
	}

	private sealed class Section(string key, string display, FoldTitle title, Section? parent)
	{
		public string Key { get; } = key;

		public string Display { get; } = display;

		public FoldTitle Title { get; } = title;

		public Section? Parent { get; } = parent;

		public List<Field> Fields { get; } = [];

		public List<Section> Children { get; } = [];

		public List<Field> Matching { get; set; } = [];

		// This group, then each group inside it.
		public IEnumerable<Section> Subtree()
		{
			yield return this;
			foreach (var member in Children.SelectMany(child => child.Subtree())) yield return member;
		}
	}

	private sealed class Field(
		string category,
		string path,
		string label,
		string description,
		string original,
		SettingsFieldKind kind,
		Visual control)
	{
		public string Category { get; } = category;

		public string Path { get; } = path;

		public string Label { get; } = label;

		public string Description { get; } = description;

		public SettingsFieldKind Kind { get; } = kind;

		public string Original { get; set; } = original;

		public Visual Control { get; } = control;

		public bool Editable { get; set; }

		public string? Rejected { get; set; }

		public Visual? Row { get; set; }

		public TextBlock? LabelBox { get; set; }

		public bool Focused { get; set; }

		public bool Matches(string query)
		{
			return Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
			       $"{Category}.{Path}".Contains(query, StringComparison.OrdinalIgnoreCase) ||
			       Description.Contains(query, StringComparison.OrdinalIgnoreCase);
		}
	}
}