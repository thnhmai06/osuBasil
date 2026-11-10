using System.Text.RegularExpressions;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Tabs;
using Basil.Host.Cli.Shutdown;
using Basil.Testing.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;

namespace Basil.Host.Cli.Tests.Dashboard;

/// <summary>The dashboard run on an in-memory terminal: what it shows, how the command box and Ctrl+C behave.</summary>
[Collection("CLI host")]
public sealed partial class DashboardAppTests(CliFixture fixture)
{
	[GeneratedRegex(@"\x1b\[[0-9;?]*[A-Za-z]|\x1b\][^\x07\x1b]*(\x07|\x1b\\)")]
	private static partial Regex Ansi();

	private Running Start(int width = 140, int height = 40)
	{
		var backend = new InMemoryTerminalBackend(new TerminalSize(width, height), new TerminalCapabilities
		{
			AnsiEnabled = true, ColorLevel = TerminalColorLevel.TrueColor, IsOutputRedirected = false,
			IsInputRedirected = false,
			SupportsAlternateScreen = true, SupportsRawMode = true, SupportsCursorVisibility = true,
			SupportsWindowSize = true
		});
		var terminal = Terminal.Initialize(backend, new TerminalOptions { TreatControlCAsInput = true }, true);
		var services = fixture.Host.Services;
		var model = new DashboardModel { Log = new LogBuffer(TimeProvider.System), Chat = new ChatFeed() };
		var lifetime = new Lifetime();
		var shutdown = new ShutdownController(new CtrlCMachine(TimeProvider.System, CliMode.Full), lifetime,
			NullLogger<ShutdownController>.Instance, _ => { });
		var catalog = services.GetRequiredService<CommandCatalog>();
		var app = new DashboardApp(model, new DashboardRenderer(model, new ViewFormatter(TimeZoneInfo.Utc)),
			new DashboardSampler(services, model, TimeProvider.System), services.GetRequiredService<CommandRunner>(),
			new CommandSuggestions(catalog, new FixedIds()), shutdown,
			new ThemeStore(Path.Combine(Path.GetTempPath(), "dash-" + Guid.NewGuid().ToString("N"))),
			CliMode.Full, TimeProvider.System, NullLogger<DashboardApp>.Instance);
		var stop = new CancellationTokenSource();
		var thread = new Thread(() => app.Run(terminal, stop.Token)) { IsBackground = true };
		thread.Start();
		return new Running { Backend = backend, Thread = thread, Stop = stop, Lifetime = lifetime, Model = model };
	}

	[Fact]
	public async Task Dashboard_TabBarBox()
	{
		await using var run = Start();

		await Eventually.TrueAsync(() => run.Screen.Contains("Overview") && run.Screen.Contains("Metrics"),
			"the tab bar is drawn");
		Assert.Contains("❯", run.Screen);
	}

	[Fact]
	public async Task FunctionKey_ChangesTab()
	{
		await using var run = Start();
		await Eventually.TrueAsync(() => run.Screen.Contains("Overview"), "the dashboard is drawn");

		run.Backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.F5 });

		await Eventually.TrueAsync(() => run.Model.Tab == DashboardTab.Logs, "F5 selects the Logs tab");
	}

	[Fact]
	public async Task TypedCommand_OutputAboveBox()
	{
		await using var run = Start();
		await Eventually.TrueAsync(() => run.Screen.Contains("Overview"), "the dashboard is drawn");

		run.Backend.PushEvent(new TerminalTextEvent { Text = "status" });
		run.Backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

		await Eventually.TrueAsync(() => run.Model.Output.Any(line => line.Text.StartsWith("version:")),
			"the status output is shown");
		Assert.Contains(run.Model.Output, line => line.Text == "❯ status");
	}

	[Fact]
	public async Task TypedPassword_HiddenInHistory()
	{
		await using var run = Start();
		await Eventually.TrueAsync(() => run.Screen.Contains("Overview"), "the dashboard is drawn");
		var name = CliFixture.Name("dash");

		run.Backend.PushEvent(new TerminalTextEvent { Text = $"users create --username {name} --password hunter2" });
		run.Backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

		await Eventually.TrueAsync(() => run.Model.Output.Any(line => line.Text.StartsWith("Created")),
			"the user is created");
		Assert.DoesNotContain(run.Model.Output, line => line.Text.Contains("hunter2"));
		Assert.Contains(run.Model.Output, line => line.Text.Contains("--password ***"));
	}

	[Fact]
	public async Task CtrlCTwice_AsksAndStops()
	{
		await using var run = Start();
		await Eventually.TrueAsync(() => run.Screen.Contains("Overview"), "the dashboard is drawn");

		run.Backend.PushEvent(new TerminalKeyEvent
			{ Key = TerminalKey.Unknown, Char = 'c', Modifiers = TerminalModifiers.Ctrl });
		await Eventually.TrueAsync(() => run.Model.Notice.StartsWith("Press Ctrl+C again within 3 seconds"),
			"the first Ctrl+C asks for confirmation");
		run.Backend.PushEvent(new TerminalKeyEvent
			{ Key = TerminalKey.Unknown, Char = 'c', Modifiers = TerminalModifiers.Ctrl });

		await Eventually.TrueAsync(() => run.Lifetime.Stops == 1, "the second Ctrl+C stops the application");
	}

	private sealed class Lifetime : IHostApplicationLifetime
	{
		public int Stops;
		public CancellationToken ApplicationStarted => CancellationToken.None;
		public CancellationToken ApplicationStopping => CancellationToken.None;
		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
			Interlocked.Increment(ref Stops);
		}
	}

	private sealed class Running : IAsyncDisposable
	{
		public required InMemoryTerminalBackend Backend { get; init; }
		public required Thread Thread { get; init; }
		public required CancellationTokenSource Stop { get; init; }
		public required Lifetime Lifetime { get; init; }
		public required DashboardModel Model { get; init; }

		public string Screen => Ansi().Replace(Backend.GetOutText(), string.Empty);

		public async ValueTask DisposeAsync()
		{
			await Stop.CancelAsync();
			Thread.Join(TimeSpan.FromSeconds(10));
		}
	}

	private sealed class FixedIds : ISuggestionSource
	{
		public IEnumerable<string> Ids(string resource)
		{
			return [];
		}
	}
}