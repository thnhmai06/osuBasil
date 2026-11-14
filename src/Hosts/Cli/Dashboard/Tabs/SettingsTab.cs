using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Group = XenoAtom.Terminal.UI.Controls.Group;
using Basil.Application.Servers.Messages;
using Basil.Application.Users.Registry;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard.Shell;
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
///     The server settings: a strip of categories, one form per category that saves each field when the operator
///     presses Enter or leaves it, the description of the field in focus beside it, and a read-only page of the
///     configuration files.
/// </summary>
internal sealed partial class SettingsTab : ITabView
{
	private const string Configuration = "Configuration";
	private const string StripHelp = "Down or Enter edits the fields of the category; Left and Right change the category.";

	private static readonly Regex Secret = SecretPattern();

	private readonly Group _content = new();
	private readonly ConsoleTab _console;
	private readonly State<string> _description = new(string.Empty);
	private readonly State<string> _title = new(string.Empty);
	private readonly State<string> _path = new(string.Empty);
	private readonly List<Field> _fields = [];
	private readonly Glyphs _glyphs;
	private readonly ILogger _logger;
	private readonly Palette _palette;
	private readonly CliOptions _options;
	private readonly Action<Action> _post;
	private readonly IServiceProvider _services;
	private readonly State<int> _selected = new(0);
	private readonly string[] _categories;
	private readonly TabControl _strip;
	private TerminalApp? _app;
	private int _loaded = -1;
	private int _version;
	private int _saving;
	private bool _reload;

	/// <summary>Creates the tab.</summary>
	public SettingsTab(IServiceProvider services, ConsoleTab console, CliOptions options, Glyphs glyphs,
		Action<Action> post, ILogger logger)
	{
		_services = services;
		_console = console;
		_options = options;
		_glyphs = glyphs;
		_post = post;
		_logger = logger;
		_palette = new Palette(options.NoColor);
		_categories = [.. SettingsCategories.Names, Configuration];
		_strip = new TabControl(_categories.Select(name => new TabPage(name, new TextBlock(string.Empty))).ToArray())
			.Style(options.Ascii ? TabControlStyle.Ascii : TabControlStyle.NoBorder).SelectedIndex(_selected);
		_strip.AddCommand(new Command
		{
			Id = "settings.strip.down", LabelMarkup = "Edit fields", Gesture = new KeyGesture(TerminalKey.Down),
			CanExecute = _ => _fields.Count > 0, ConsumesGestureWhenUnavailable = false, Execute = _ => FocusField(0)
		});
		_strip.AddCommand(new Command
		{
			Id = "settings.strip.enter", LabelMarkup = "Edit fields", Gesture = new KeyGesture(TerminalKey.Enter),
			CanExecute = _ => _fields.Count > 0, ConsumesGestureWhenUnavailable = false, Execute = _ => FocusField(0)
		});
		var description = new Group().Style(GroupStyle.Rounded).Padding(new XenoAtom.Terminal.UI.Geometry.Thickness(1, 0, 1, 0))
			.Content(new VStack(new TextBlock(() => _title.Value), new Markup(() => _path.Value),
				new TextBlock(() => _description.Value).Wrap(true)))
			.Stretch();
		var body = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star(2) }, new ColumnDefinition { Width = GridLength.Star(1) })
			.Rows(new RowDefinition { Height = GridLength.Star() })
			.Cell(_content.Style(GroupStyle.Rounded).Stretch(), 0, 0)
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
		if (_selected.Value != _loaded) Load();

		var anyFocused = false;
		foreach (var field in _fields)
		{
			var focused = field.Control.HasFocus;
			if (focused)
			{
				anyFocused = true;
				_title.Value = field.Label;
				_path.Value = _palette.Wrap(Ink.Muted, $"{Category}.{field.Path}");
				_description.Value = SettingsDescriptions.Of($"{Category}.{field.Path}") ?? string.Empty;
			}
			else if (field.Focused)
			{
				Save(field);
			}

			field.Focused = focused;
		}

		if (!anyFocused && _strip.HasFocus) ShowStripHelp();
	}

	/// <inheritdoc />
	public void Refresh()
	{
		Load();
	}

	private string Category => _categories[Math.Clamp(_selected.Value, 0, _categories.Length - 1)];

	private void ShowStripHelp()
	{
		_title.Value = Category;
		_path.Value = string.Empty;
		_description.Value = Category == Configuration
			? "Read from Configurations/appsettings.json; edit the file and restart."
			: StripHelp;
	}

	private void FocusField(int index)
	{
		if (index < 0)
		{
			_app?.Focus(_strip);
			return;
		}

		if (index >= _fields.Count) return;

		if (_fields[index].Control is PromptEditor editor) editor.CaretIndex = (editor.Text ?? string.Empty).Length;
		_app?.Focus(_fields[index].Control);
	}

	private void Load()
	{
		if (_saving > 0)
		{
			_reload = true;
			return;
		}

		_loaded = _selected.Value;
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
				var list = new VStack().Spacing(0);
				var readOnly = category == Configuration || !_options.Editable;
				foreach (var (path, value, kind) in rows)
				{
					var label = category == Configuration ? path : SettingsLabels.Of($"{category}.{path}") ?? path;
					Visual control;
					if (readOnly)
						control = new TextBlock(Palette.Plain(value));
					else if (kind == FieldKind.Bool)
						control = new Switch().IsOn(value == "true");
					else
						control = new PromptEditor().LineMode(PromptEditorLineMode.SingleLine).PromptMarkup(string.Empty)
							.Text(value).MinWidth(24);

					var field = new Field(category, path, label, value, kind, control);
					if (!readOnly)
					{
						if (control is PromptEditor editor) editor.Accepted(() => Save(field));
						else if (control is Switch toggle) toggle.Toggled(() => Save(field));
						AddKeys(field);
						_fields.Add(field);
					}

					list.Add(new HStack(new TextBlock(Palette.Plain(label)).MinWidth(SettingsLabels.MaxLength).MaxWidth(SettingsLabels.MaxLength),
						control.Stretch()).Spacing(1));
					list.Add(new Markup(() => field.Result.Value.Length == 0 ? string.Empty : $"{new string(' ', SettingsLabels.MaxLength + 1)}{field.Result.Value}"));
				}

				_content.Content(new ScrollViewer(list));
				ShowStripHelp();
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "The settings could not be shown");
			}
		});
	}

	// Up and Down move between the fields, Up on the first one and Esc or Alt+Left go back to the strip; Esc also drops
	// an edit that was not saved.
	private void AddKeys(Field field)
	{
		var id = $"settings.{field.Category}.{field.Path}";
		void Add(string name, KeyGesture gesture, Action run)
		{
			field.Control.AddCommand(new Command
			{
				Id = $"{id}.{name}", LabelMarkup = name, Gesture = gesture, Execute = _ => run()
			});
		}

		Add("down", new KeyGesture(TerminalKey.Down), () => FocusField(Math.Min(_fields.IndexOf(field) + 1, _fields.Count - 1)));
		Add("up", new KeyGesture(TerminalKey.Up), () => FocusField(_fields.IndexOf(field) - 1));
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
		if (text == field.Original)
		{
			if (field.Invalid) field.Result.Value = string.Empty;
			field.Invalid = false;
			return;
		}

		var json = ToJson(field.Kind, text);
		if (json is null)
		{
			field.Result.Value = _palette.Wrap(Ink.Error, "Enter a number.");
			field.Invalid = true;
			return;
		}

		field.Original = text;
		field.Invalid = false;
		field.Result.Value = string.Empty;
		_saving++;
		_ = _console.RunCommand($"settings {field.Category} set --{field.Path} {Quote(json)}", line => ShowResult(field, line))
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

	// The output of the command goes to the log as well; here the field shows how it ended.
	private void ShowResult(Field field, OutputLine line)
	{
		var ink = line.Kind switch
		{
			OutputKind.Error => Ink.Error,
			OutputKind.Ok => Ink.Success,
			_ => Ink.Muted
		};
		var text = line.Text.Split('\n')[0].TrimEnd('\r');
		_post(() =>
		{
			if (ink != Ink.Muted || field.Result.Value.Length == 0) field.Result.Value = _palette.Wrap(ink, text);
		});
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

	private sealed class Field(string category, string path, string label, string original, FieldKind kind, Visual control)
	{
		public string Category { get; } = category;

		public string Path { get; } = path;

		public string Label { get; } = label;

		public FieldKind Kind { get; } = kind;

		public string Original { get; set; } = original;

		public Visual Control { get; } = control;

		public bool Focused { get; set; }

		public bool Invalid { get; set; }

		public State<string> Result { get; } = new(string.Empty);
	}
}
