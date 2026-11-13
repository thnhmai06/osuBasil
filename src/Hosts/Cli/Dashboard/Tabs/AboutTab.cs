using System.Globalization;
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
	private readonly IUpdateStatus _updates;
	private readonly Action<Action> _post;
	private readonly ILogger _logger;
	private readonly State<string> _status = new(string.Empty);
	private bool _checking;

	/// <summary>Creates the tab.</summary>
	public AboutTab(IServiceProvider services, Glyphs glyphs, Action<Action> post, ILogger logger)
	{
		_updates = services.GetRequiredService<IUpdateStatus>();
		_post = post;
		_logger = logger;
		var banner = glyphs.Ascii ? (Visual)new TextBlock("basil") : new TextFiglet("Basil");
		var about = new Group("About").Style(glyphs.Ascii ? GroupStyle.Ascii : GroupStyle.Rounded)
			.Padding(new Thickness(1, 0, 1, 0)).Content(new VStack(banner,
				new TextBlock("osu!Basil — A lightweight osu! (stable) server for multiplayer."),
				new TextBlock($"Version  {AppVersion.Current}"),
				new TextBlock("GitHub   https://github.com/thnhmai06/osuBasil"),
				new TextBlock("License  MIT")).Spacing(0));
		var updates = new Group("Updates").Style(glyphs.Ascii ? GroupStyle.Ascii : GroupStyle.Rounded)
			.Padding(new Thickness(1, 0, 1, 0)).Content(new VStack(new TextBlock(() => _status.Value),
				new Button("Check now").Click(CheckNow)).Spacing(0));
		View = new VStack(about, updates).Spacing(0).Stretch();
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
		_status.Value = "Checking...";
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
		_status.Value = $"Basil       {Text(report.Basil, report.BasilVersion)}\n" +
		                $"Rulesets    {Text(report.Rulesets, report.RulesetsVersion)}\n" +
		                $"Last check  {last}";
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
