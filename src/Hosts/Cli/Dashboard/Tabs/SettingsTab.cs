using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Group = XenoAtom.Terminal.UI.Controls.Group;
using Basil.Application.Servers.Messages;
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
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>
///     The server settings: a strip of categories, one form per category with a search box and fields grouped under
///     foldable titles that saves each field when the operator presses Enter or leaves it, the description of the field
///     in focus beside it, and a read-only page of the configuration files.
/// </summary>
internal sealed partial class SettingsTab : ITabView
{
	private const string Configuration = "Configuration";
	private const string StripHelp = "Down or Enter goes to the settings of the category; Left and Right change the category.";
	private const string NotNumber = "Enter a number.";

	private static readonly Regex Secret = SecretPattern();

	private readonly Group _content = new();
	private readonly ConsoleTab _console;
	private readonly State<string> _description = new(string.Empty);
	private readonly State<string> _title = new(string.Empty);
	private readonly State<string> _path = new(string.Empty);
	private readonly List<Field> _fields = [];
	private readonly List<Section> _sections = [];
	private readonly List<Visual> _nav = [];
	private readonly HashSet<string> _closed = [];
	private readonly HashSet<string> _searchClosed = [];
	private readonly State<string> _query = new(string.Empty);
	private readonly ScrollViewer _scroll = new() { HorizontalScrollEnabled = false };
	private readonly TextBox _search;
	private readonly ShellKit _kit;
	private string _applied = string.Empty;
	private readonly Glyphs _glyphs;
	private readonly ILogger _logger;
	private readonly Palette _palette;
	private readonly CliOptions _options;
	private readonly Action<Action> _post;
	private readonly IServiceProvider _services;
	private readonly State<int> _selected = new(0);
	private readonly string[] _categories;
	private readonly TabControl _strip;
	private readonly TabSpread _spread;
	private TerminalApp? _app;
	private int _loaded = -1;
	private int _labelWidth;
	private int _version;
	private int _saving;
	private bool _reload;

	/// <summary>Creates the tab.</summary>
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
		_categories = [.. SettingsCategories.Names, Configuration];
		var stripStyle = options.Ascii ? TabControlStyle.Ascii : TabControlStyle.NoBorder;
		_strip = new TabControl(_categories.Select(name => new TabPage(Words.Humanise(name), new TextBlock(string.Empty))).ToArray())
			.Style(stripStyle).SelectedIndex(_selected);
		_spread = new TabSpread(_strip, stripStyle, _categories.Select(Words.Humanise));
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
		var description = new Group().Style(GroupStyle.Rounded).Padding(new XenoAtom.Terminal.UI.Geometry.Thickness(1, 0, 1, 0))
			.Content(new ScrollViewer(new VStack(new TextBlock(() => _title.Value).Wrap(true), new Markup(() => _path.Value),
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
		_content.Style(GroupStyle.Rounded).Padding(new XenoAtom.Terminal.UI.Geometry.Thickness(1, 0, 1, 0))
			.Content(new Grid().Columns(new ColumnDefinition { Width = GridLength.Star() })
				.Rows(new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star() })
				.Cell(searchRow, 0, 0).Cell(WheelScroll.Accelerate(_scroll, clock).Stretch(), 1, 0)).Stretch();
		foreach (var gesture in Dialogs.BackGestures)
			_content.AddCommand(new Command
			{
				Id = $"settings.back.{gesture}", LabelMarkup = "Back", Gesture = gesture,
				CanExecute = _ => ReferenceEquals(_app?.FocusedElement, _search), ConsumesGestureWhenUnavailable = false,
				Execute = _ =>
				{
					if (_query.Value.Length > 0) _query.Value = string.Empty;
					else _app?.Focus(_strip);
				}
			});
		var body = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star(2) }, new ColumnDefinition { Width = GridLength.Star(1) })
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

	/// <inheritdoc />
	public Visual View { get; }

	/// <inheritdoc />
	public void Attach(TerminalApp app)
	{
		_app = app;
	}

	/// <inheritdoc />
	public void Activate()
	{
		// An edit left unsaved when the operator switched tabs is saved before the form is read again.
		foreach (var field in _fields) Save(field);
		Load();
		_app?.Focus(_strip);
		ShowStripHelp();
	}

	/// <inheritdoc />
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
				_path.Value = _palette.Wrap(Ink.Muted, $"{Category}.{field.Path}");
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
			_title.Value = Words.Humanise(title.Name);
			_path.Value = string.Empty;
			_description.Value = "Enter, Left and Right open and fold the group.";
		}

		if (!anyFocused && (_strip.HasFocus || ReferenceEquals(_app?.FocusedElement, _search))) ShowStripHelp();
	}

	/// <inheritdoc />
	public void Refresh()
	{
		Load();
	}

	private string Category => _categories[Math.Clamp(_selected.Value, 0, _categories.Length - 1)];

	private void ShowStripHelp()
	{
		_title.Value = Words.Humanise(Category);
		_path.Value = string.Empty;
		_description.Value = Category == Configuration
			? "Read from Configurations/appsettings.json; edit the file and restart."
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
		var category = Category;
		var version = Interlocked.Increment(ref _version);
		if (category == Configuration)
		{
			Show(version, category, ConfigurationRows());
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

				var node = JsonSerializer.SerializeToNode(SettingsCategories.Get(category, settings), ApiJson.Options);
				var rows = new List<Row>();
				Flatten(rows, string.Empty, node);
				Show(version, category, rows);
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "The settings could not be loaded");
			}
		});
	}

	private void Show(int version, string category, IReadOnlyList<Row> rows)
	{
		_post(() =>
		{
			if (version != _version) return;

			try
			{
				_fields.Clear();
				_sections.Clear();
				var list = new VStack().Spacing(0);
				var readOnly = category == Configuration || !_options.Editable;
				foreach (var (path, value, kind) in rows)
				{
					var (group, label) = category == Configuration ? Split(path) : (GroupOf(category, path),
						SettingsLabels.Of($"{category}.{path}") ?? path);
					var section = _sections.Find(s => s.Name == group);
					if (section is null)
					{
						var title = new FoldTitle(Words.Humanise(group), _glyphs, _options.NoColor);
						var created = new Section(group, title);
						title.Folding += open => Fold(created, open);
						AddKeys(title, $"settings.{category}.{group}");
						_sections.Add(section = created);
						list.Add(title);
					}

					Visual control;
					if (readOnly)
						control = new TextBlock(Palette.Plain(value)).Trimming(TextTrimming.EndEllipsis);
					else if (kind == FieldKind.Bool)
						control = new Switch().IsOn(value == "true");
					else
						control = new PromptEditor().LineMode(PromptEditorLineMode.SingleLine).PromptMarkup(string.Empty)
							.Text(value);

					var full = $"{category}.{path}";
					var field = new Field(category, path, label, SettingsDescriptions.Of(full) ?? string.Empty, value, kind,
						control);
					if (!readOnly)
					{
						if (control is PromptEditor editor) editor.Accepted(() => Save(field));
						else if (control is Switch toggle) toggle.Toggled(() => Save(field));
						AddKeys(control, $"settings.{category}.{path}");
						AddEscape(field);
						field.Editable = true;
						_fields.Add(field);
					}

					// Every label has the width of the label column, so every row of every group lines up.
					field.LabelBox = new TextBlock(Palette.Plain(Relative(label, group, category))).Trimming(TextTrimming.EndEllipsis)
						.Margin(new XenoAtom.Terminal.UI.Geometry.Thickness(0, 0, 1, 0));
					field.Row = new HStack(field.LabelBox, control.Stretch()).Spacing(0);
					section.Fields.Add(field);
					list.Add(field.Row);
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

	// The label column takes a little over half of the room of the fields, whatever the size of the terminal.
	private void SizeLabels()
	{
		var width = Math.Clamp(_scroll.Bounds.Width * 5 / 9, 12, SettingsLabels.MaxLength);
		if (width == _labelWidth) return;

		_labelWidth = width;
		foreach (var section in _sections)
			foreach (var field in section.Fields)
				field.LabelBox!.MinWidth(width).MaxWidth(width);
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

	// Splits a key of the configuration at its first colon: the section it belongs to and the rest of its name.
	private static (string Group, string Label) Split(string key)
	{
		var colon = key.IndexOf(':');
		return colon < 0 ? (Configuration, key) : (key[..colon], key[(colon + 1)..]);
	}

	// A setting under a nested object belongs to the group of that object; one on the category itself, to the category.
	private static string GroupOf(string category, string path)
	{
		var dot = path.IndexOf('.');
		return dot < 0 ? category : path[..dot];
	}

	private void Fold(Section section, bool open)
	{
		var folded = _applied.Trim().Length > 0 ? _searchClosed : _closed;
		var key = $"{Category}.{section.Name}";
		if (open) folded.Remove(key);
		else folded.Add(key);
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
		{
			var matches = section.Fields.Where(field => !searching || field.Matches(query)).ToList();
			var open = !folded.Contains($"{Category}.{section.Name}");
			section.Title.Open = open;
			section.Title.Count = matches.Count;
			section.Title.IsVisible = matches.Count > 0;
			if (matches.Count > 0) _nav.Add(section.Title);
			foreach (var field in section.Fields)
			{
				var shown = open && matches.Contains(field);
				field.Row!.IsVisible = shown;
				if (shown && field.Editable) _nav.Add(field.Control);
			}
		}
	}

	// Up and Down move between the titles and the fields, Up on the first one goes to the search box.
	private void AddKeys(Visual visual, string id)
	{
		visual.AddCommand(new Command
		{
			Id = $"{id}.down", LabelMarkup = "down", Gesture = new KeyGesture(TerminalKey.Down), Execute = _ => Step(visual, 1)
		});
		visual.AddCommand(new Command
		{
			Id = $"{id}.up", LabelMarkup = "up", Gesture = new KeyGesture(TerminalKey.Up), Execute = _ => Step(visual, -1)
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

	/// <summary>Writes what the operator typed as the JSON the command reads, or <see langword="null" /> when it is not valid for the kind.</summary>
	private static string? ToJson(FieldKind kind, string text)
	{
		switch (kind)
		{
			case FieldKind.Bool:
				return text;
			case FieldKind.Number:
				return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
					? number.ToString(CultureInfo.InvariantCulture)
					: null;
			case FieldKind.List:
				return new JsonArray([.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
					.Select(item => (JsonNode?)JsonValue.Create(item))]).ToJsonString();
			case FieldKind.Optional when text.Length == 0:
				return "null";
			default:
				return JsonValue.Create(text).ToJsonString();
		}
	}

	/// <summary>Writes a value as one word of a command line.</summary>
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

	private IReadOnlyList<Row> ConfigurationRows()
	{
		var configuration = _services.GetRequiredService<IConfiguration>();
		return
		[
			.. configuration.AsEnumerable().Where(pair => pair.Value is not null).OrderBy(pair => pair.Key)
				.Select(pair => new Row(pair.Key, Secret.IsMatch(pair.Key) ? "••••" : pair.Value!, FieldKind.Text))
		];
	}

	private static void Flatten(List<Row> rows, string prefix, JsonNode? node)
	{
		switch (node)
		{
			case JsonObject obj:
				foreach (var (key, value) in obj) Flatten(rows, prefix.Length == 0 ? key : $"{prefix}.{key}", value);
				break;
			case JsonValue value:
				rows.Add(new Row(prefix, value.ToString(), value.GetValueKind() switch
				{
					JsonValueKind.True or JsonValueKind.False => FieldKind.Bool,
					JsonValueKind.Number => FieldKind.Number,
					_ => FieldKind.Text
				}));
				break;
			case null when prefix.Length > 0:
				rows.Add(new Row(prefix, string.Empty, FieldKind.Optional));
				break;
			case JsonArray array:
				rows.Add(new Row(prefix, string.Join(", ", array.Select(item => item?.ToString())), FieldKind.List));
				break;
		}
	}

	[GeneratedRegex("password|secret|token|key|connectionstring|cert", RegexOptions.IgnoreCase)]
	private static partial Regex SecretPattern();

	/// <summary>What a setting holds, which decides how its text is written back.</summary>
	private enum FieldKind : byte
	{
		/// <summary>Text.</summary>
		Text,

		/// <summary>A number.</summary>
		Number,

		/// <summary>On or off.</summary>
		Bool,

		/// <summary>Comma-separated text that is a list.</summary>
		List,

		/// <summary>Text, or nothing.</summary>
		Optional
	}

	private sealed record Row(string Path, string Value, FieldKind Kind);

	private sealed class Section(string name, FoldTitle title)
	{
		public string Name { get; } = name;

		public FoldTitle Title { get; } = title;

		public List<Field> Fields { get; } = [];
	}

	private sealed class Field(string category, string path, string label, string description, string original,
		FieldKind kind, Visual control)
	{
		public string Category { get; } = category;

		public string Path { get; } = path;

		public string Label { get; } = label;

		public string Description { get; } = description;

		public FieldKind Kind { get; } = kind;

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
