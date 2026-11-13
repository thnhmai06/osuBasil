using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Group = XenoAtom.Terminal.UI.Controls.Group;
using Basil.Application.Servers.Messages;
using Basil.Application.Users.Registry;
using Basil.Host.Cli.Dashboard.Shell;
using Basil.Host.Shared.Json;
using Basil.Host.Shared.Views;
using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>
///     The server settings: one form per category that saves each field when the operator presses Enter or leaves it,
///     the description of the field in focus beside it, and a read-only page of the configuration files.
/// </summary>
internal sealed partial class SettingsTab : ITabView
{
	private const string Configuration = "Configuration";

	private static readonly Regex Secret = SecretPattern();

	private readonly Group _content = new();
	private readonly ConsoleTab _console;
	private readonly State<string> _description = new(string.Empty);
	private readonly State<string> _title = new(string.Empty);
	private readonly List<Field> _fields = [];
	private readonly Glyphs _glyphs;
	private readonly ILogger _logger;
	private readonly CliOptions _options;
	private readonly Action<Action> _post;
	private readonly IServiceProvider _services;
	private readonly State<int> _selected = new(0);
	private readonly string[] _categories;
	private readonly TabControl _strip;
	private int _loaded = -1;
	private int _version;

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
		_categories = [.. SettingsCategories.Names, Configuration];
		_strip = new TabControl(_categories.Select(name => new TabPage(name, new TextBlock(string.Empty))).ToArray())
			.Style(options.Ascii ? TabControlStyle.Ascii : TabControlStyle.NoBorder).SelectedIndex(_selected);
		var description = new Group().Style(GroupStyle.Rounded).Padding(new XenoAtom.Terminal.UI.Geometry.Thickness(1, 0, 1, 0))
			.Content(new VStack(new TextBlock(() => _title.Value), new TextBlock(() => _description.Value).Wrap(true)))
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
	}

	/// <inheritdoc />
	public Visual View { get; }

	/// <inheritdoc />
	public void Attach(TerminalApp app)
	{
	}

	/// <inheritdoc />
	public void Activate()
	{
		Load();
	}

	/// <inheritdoc />
	public void Tick()
	{
		if (_selected.Value != _loaded) Load();

		foreach (var field in _fields)
		{
			var focused = field.Control.HasFocus;
			if (focused)
			{
				_title.Value = field.Path;
				_description.Value = SettingsDescriptions.All.GetValueOrDefault($"{Category}.{field.Path}") ??
				                     SettingsDescriptions.All.GetValueOrDefault(Category) ?? string.Empty;
			}
			else if (field.Focused)
			{
				Save(field);
			}

			field.Focused = focused;
		}
	}

	/// <inheritdoc />
	public void Refresh()
	{
		Load();
	}

	private string Category => _categories[Math.Clamp(_selected.Value, 0, _categories.Length - 1)];

	private void Load()
	{
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
				var rows = new List<(string, string, bool)>();
				Flatten(rows, string.Empty, node);
				Show(version, category, rows);
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "The settings could not be loaded");
			}
		});
	}

	private void Show(int version, string category, IReadOnlyList<(string Path, string Value, bool IsBool)> rows)
	{
		_post(() =>
		{
			if (version != _version) return;

			try
			{
			_fields.Clear();
			var list = new VStack().Spacing(0);
			var readOnly = category == Configuration || !_options.Editable;
			foreach (var (path, value, isBool) in rows)
			{
				Visual control;
				if (readOnly)
				{
					control = new TextBlock(Palette.Plain(value));
				}
				else if (isBool)
				{
					control = new Switch().IsOn(value == "true");
				}
				else
				{
					control = new PromptEditor().LineMode(PromptEditorLineMode.SingleLine).PromptMarkup(string.Empty).Text(value).MinWidth(24);
				}

				var field = new Field(category, path, value, control);
				if (!readOnly)
				{
					if (control is PromptEditor editor) editor.Accepted(() => Save(field));
					else if (control is Switch toggle) toggle.Toggled(() => Save(field));
					_fields.Add(field);
				}

				list.Add(new HStack(new TextBlock($"{path,-34}").MinWidth(34), control.Stretch()).Spacing(1));
			}

			_content.Content(new ScrollViewer(list));
			_description.Value = category == Configuration
				? "Read from Configurations/appsettings.json; edit the file and restart."
				: string.Empty;
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "The settings could not be shown");
			}
		});
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

		field.Original = text;
		var quoted = text.Contains(' ') ? $"\"{text.Replace("\"", "'")}\"" : text;
		_console.RunCommand($"settings {field.Category} set --{field.Path} {quoted}");
	}

	private IReadOnlyList<(string, string, bool)> ConfigurationRows()
	{
		var configuration = _services.GetRequiredService<IConfiguration>();
		return
		[
			.. configuration.AsEnumerable().Where(pair => pair.Value is not null).OrderBy(pair => pair.Key)
				.Select(pair => (pair.Key, Secret.IsMatch(pair.Key) ? "••••" : pair.Value!, false))
		];
	}

	private static void Flatten(List<(string, string, bool)> rows, string prefix, JsonNode? node)
	{
		switch (node)
		{
			case JsonObject obj:
				foreach (var (key, value) in obj) Flatten(rows, prefix.Length == 0 ? key : $"{prefix}.{key}", value);
				break;
			case JsonValue value:
				rows.Add((prefix, value.ToString(), value.GetValueKind() is JsonValueKind.True or JsonValueKind.False));
				break;
			case null when prefix.Length > 0:
				rows.Add((prefix, string.Empty, false));
				break;
			case JsonArray array:
				rows.Add((prefix, string.Join(", ", array.Select(item => item?.ToString())), false));
				break;
		}
	}

	[GeneratedRegex("password|secret|token|key|connectionstring|cert", RegexOptions.IgnoreCase)]
	private static partial Regex SecretPattern();

	private sealed class Field(string category, string path, string original, Visual control)
	{
		public string Category { get; } = category;

		public string Path { get; } = path;

		public string Original { get; set; } = original;

		public Visual Control { get; } = control;

		public bool Focused { get; set; }
	}
}
