using System.Globalization;
using System.Reflection;
using Basil.Host.Cli.Dashboard.Shell;
using Basil.Host.Shared.Info;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>What Basil is, which version runs, and whether a newer release exists.</summary>
internal sealed class AboutTab : ITabView
{
	// Original art by Joan G. Stark (Spunk), edited by Mai Thành (thnhmai06).
	private const string Art =
		"""
		                    _
		                  _(_)_          ____            _wW̲w   _
		      @@@@       (_)@(_)   vVVVv| __ )  __ _ ___(_) |) _(_)_
		     @@()@@ wWWWw  (_)\    (___)|  _ \ / _` / __| | | (_)@(_)
		      @@@@  (___)     `|/    Y  | |_) | (_| \__ \ | |   (_)\
		       /      Y       \|    \|/ |____/ \__,_|___/_|_|      |
		    \ |     \ |/       | / \ | /  \|/       |/    \|      \|/
		    \\|//   \\|///  \\\|//\\\|/// \|///  \\\|//  \\|//  \\\|//
		^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
		""";

	private static readonly string Description =
		Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? string.Empty;

	private readonly IUpdateStatus _updates;
	private readonly Action<Action> _post;
	private readonly ILogger _logger;
	private readonly State<string> _basil = new(string.Empty);
	private readonly State<string> _last = new(string.Empty);
	private readonly State<string> _rulesets = new(string.Empty);
	private bool _checking;

	/// <summary>Creates the tab.</summary>
	public AboutTab(IServiceProvider services, Glyphs glyphs, CliOptions options, Action<Action> post, ILogger logger)
	{
		_updates = services.GetRequiredService<IUpdateStatus>();
		_post = post;
		_logger = logger;
		var style = glyphs.Ascii ? GroupStyle.Ascii : GroupStyle.Rounded;
		var palette = new Palette(options.NoColor);
		var info = new VStack(
			new Markup(palette.Wrap(Ink.Accent, "osu!Basil")),
			new TextBlock(Description).Wrap(true),
			new TextBlock(string.Empty),
			new Markup($"{palette.Wrap(Ink.Muted, "Version")}  {palette.Wrap(Ink.Text, AppVersion.Current)}"),
			new Markup($"{palette.Wrap(Ink.Muted, "GitHub ")}  {palette.Wrap(Ink.Text, "https://github.com/thnhmai06/osuBasil")}"),
			new Markup($"{palette.Wrap(Ink.Muted, "License")}  {palette.Wrap(Ink.Text, "MIT")}")).Spacing(0).Stretch();
		var about = new Group("About").Style(style).Padding(new Thickness(1, 0, 1, 0))
			.Content(new HStack(new TextBlock(glyphs.Ascii ? Art.Replace("W\u0332", "W") : Art), info).Spacing(3));
		var updates = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star() }, new ColumnDefinition { Width = GridLength.Star() })
			.Rows(new RowDefinition { Height = GridLength.Auto })
			.Cell(Panel("Basil", _basil, style), 0, 0)
			.Cell(Panel("Rulesets", _rulesets, style), 0, 1);
		var check = new HStack(new Button("Check now").Click(CheckNow), new TextBlock(() => _last.Value)).Spacing(2);
		View = new VStack(about, updates, check).Spacing(0).Stretch();
		Show();
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
		Show();
	}

	/// <inheritdoc />
	public void Tick()
	{
	}

	/// <inheritdoc />
	public void Refresh()
	{
		Show();
	}

	private void CheckNow()
	{
		if (_checking) return;

		_checking = true;
		_basil.Value = _rulesets.Value = "Checking...";
		_ = Task.Run(async () =>
		{
			try
			{
				await _updates.CheckNowAsync();
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "The check for updates failed");
			}

			_post(() =>
			{
				_checking = false;
				Show();
			});
		});
	}

	private void Show()
	{
		if (_checking) return;

		var report = _updates.Status;
		var last = _updates.LastCheck is { } time
			? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
			: "never";
		_basil.Value = Text(report.Basil, report.BasilVersion);
		_rulesets.Value = Text(report.Rulesets, report.RulesetsVersion);
		_last.Value = $"Last check  {last}";
	}

	private static Group Panel(string title, State<string> text, GroupStyle style)
	{
		return new Group(title).Style(style).Padding(new Thickness(1, 0, 1, 0)).Content(new TextBlock(() => text.Value));
	}

	private static string Text(UpdateState state, string? version)
	{
		return state switch
		{
			UpdateState.UpToDate => "up to date",
			UpdateState.Available => $"{version} available",
			UpdateState.Skipped => "not checked (switched off or not installed by the updater)",
			UpdateState.Failed => "check failed",
			_ => "not checked yet"
		};
	}
}
