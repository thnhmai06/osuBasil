using System.Text.RegularExpressions;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Tabs;
using Basil.Host.Cli.Shutdown;
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

	[GeneratedRegex(@"\x1b\[[0-9;?]*[A-Za-z]|\x1b\][^\x07\x1b]*(\x07|\x1b\\)")]
	private static partial Regex Ansi();

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

	private Running Start(int width = 140, int height = 40)
	{
		var backend = new InMemoryTerminalBackend(new TerminalSize(width, height), new TerminalCapabilities
		{
			AnsiEnabled = true, ColorLevel = TerminalColorLevel.TrueColor, IsOutputRedirected = false, IsInputRedirected = false,
			SupportsAlternateScreen = true, SupportsRawMode = true, SupportsCursorVisibility = true, SupportsWindowSize = true
		});
		var terminal = Terminal.Initialize(backend, new TerminalOptions { TreatControlCAsInput = true }, true);
		var services = fixture.Host.Services;
		var model = new DashboardModel { Log = new LogBuffer(TimeProvider.System), Chat = new ChatFeed() };
		var lifetime = new Lifetime();
		var shutdown = new ShutdownController(new CtrlCMachine(TimeProvider.System, CliMode.Full), lifetime, NullLogger<ShutdownController>.Instance, _ => { });
		var catalog = services.GetRequiredService<CommandCatalog>();
		var app = new DashboardApp(model, new DashboardRenderer(model, new ViewFormatter(TimeZoneInfo.Utc)),
			new DashboardSampler(services, model, TimeProvider.System), services.GetRequiredService<CommandRunner>(),
			new CommandSuggestions(catalog, new FixedIds()), shutdown, new ThemeStore(Path.Combine(Path.GetTempPath(), "dash-" + Guid.NewGuid().ToString("N"))),
			CliMode.Full, TimeProvider.System, NullLogger<DashboardApp>.Instance);
		var stop = new CancellationTokenSource();
		var thread = new Thread(() => app.Run(terminal, stop.Token)) { IsBackground = true };
		thread.Start();
		return new Running { Backend = backend, Thread = thread, Stop = stop, Lifetime = lifetime, Model = model };
	}

	private sealed class FixedIds : ISuggestionSource
	{
		public IEnumerable<string> Ids(string resource) => [];
	}

	// The terminal runs on a thread of its own, so a test can only wait for it by looking again; the wait is bounded.
	private static async Task<bool> WaitAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (DateTime.UtcNow < deadline)
		{
			if (condition()) return true;

			await Task.Delay(25, TestContext.Current.CancellationToken);
		}

		return condition();
	}

	[Fact]
	public async Task The_dashboard_shows_the_tab_bar_and_the_command_box()
	{
		await using var run = Start();

		Assert.True(await WaitAsync(() => run.Screen.Contains("Overview") && run.Screen.Contains("Metrics")), run.Screen);
		Assert.Contains("❯", run.Screen);
	}

	[Fact]
	public async Task A_function_key_changes_the_tab()
	{
		await using var run = Start();
		await WaitAsync(() => run.Screen.Contains("Overview"));

		run.Backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.F5 });

		Assert.True(await WaitAsync(() => run.Model.Tab == DashboardTab.Logs));
	}

	[Fact]
	public async Task A_typed_command_runs_and_its_output_appears_above_the_command_box()
	{
		await using var run = Start();
		await WaitAsync(() => run.Screen.Contains("Overview"));

		run.Backend.PushEvent(new TerminalTextEvent { Text = "status" });
		run.Backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

		Assert.True(await WaitAsync(() => run.Model.Output.Any(line => line.Text.StartsWith("version:"))));
		Assert.Contains(run.Model.Output, line => line.Text == "❯ status");
	}

	[Fact]
	public async Task A_password_in_a_typed_command_is_hidden_in_the_history_shown()
	{
		await using var run = Start();
		await WaitAsync(() => run.Screen.Contains("Overview"));
		var name = CliFixture.Name("dash");

		run.Backend.PushEvent(new TerminalTextEvent { Text = $"users create --username {name} --password hunter2" });
		run.Backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });

		Assert.True(await WaitAsync(() => run.Model.Output.Any(line => line.Text.StartsWith("Created"))));
		Assert.DoesNotContain(run.Model.Output, line => line.Text.Contains("hunter2"));
		Assert.Contains(run.Model.Output, line => line.Text.Contains("--password ***"));
	}

	[Fact]
	public async Task Ctrl_c_twice_asks_for_the_shutdown_and_stops_the_application()
	{
		await using var run = Start();
		await WaitAsync(() => run.Screen.Contains("Overview"));

		run.Backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Unknown, Char = 'c', Modifiers = TerminalModifiers.Ctrl });
		Assert.True(await WaitAsync(() => run.Model.Notice.StartsWith("Press Ctrl+C again within 3 seconds")));
		run.Backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Unknown, Char = 'c', Modifiers = TerminalModifiers.Ctrl });

		Assert.True(await WaitAsync(() => run.Lifetime.Stops == 1));
	}
}
