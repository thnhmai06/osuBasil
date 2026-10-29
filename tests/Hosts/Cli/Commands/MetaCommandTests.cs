using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Shutdown;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Basil.Host.Cli.Tests.Commands;

/// <summary>Services of the server with the control of its process replaced, so a test can shut a server down.</summary>
internal sealed class WithControl(IServiceProvider inner, IServerControl control) : IServiceProvider
{
	public object? GetService(Type serviceType)
	{
		return serviceType == typeof(IServerControl) ? control : inner.GetService(serviceType);
	}
}

internal sealed class RecordingControl : IServerControl
{
	public List<bool> Requests { get; } = [];

	public void RequestShutdown(bool restart)
	{
		Requests.Add(restart);
	}
}

internal sealed class CapturedLog : ILoggerProvider
{
	public List<string> Messages { get; } = [];

	public ILogger CreateLogger(string categoryName)
	{
		return new Logger(Messages);
	}

	public void Dispose()
	{
	}

	private sealed class Logger(List<string> messages) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			lock (messages) messages.Add(formatter(state, exception));
		}
	}
}

/// <summary>The commands that exist only on the console, and how a command is run and recorded.</summary>
[Collection("CLI host")]
public sealed class MetaCommandTests(CliFixture fixture)
{
	private (CommandRunner Runner, RecordingControl Control) RunnerWithControl(ILoggerProvider? log = null)
	{
		var control = new RecordingControl();
		var catalog = fixture.Host.Services.GetRequiredService<CommandCatalog>();
		var factory = LoggerFactory.Create(builder =>
		{
			if (log is not null) builder.AddProvider(log);
		});
		return (new CommandRunner(catalog, new WithControl(fixture.Host.Services, control), new ViewFormatter(TimeZoneInfo.Utc),
			factory.CreateLogger<CommandRunner>()), control);
	}

	private static async Task<(int Code, string Text)> RunAsync(CommandRunner runner, string line, bool running = true, CommandSession? session = null)
	{
		var output = new CollectedOutput();
		var code = await runner.RunAsync(CommandCatalog.Split(line), output, running, session, TestContext.Current.CancellationToken);
		return (code, output.Text);
	}

	[Fact]
	public async Task Help_ListsTheCommands_AndOneCommandShowsItsSyntaxAndRoute()
	{
		var (code, all) = await fixture.RunAsync("help");
		var (_, one) = await fixture.RunAsync("help users");

		Assert.Equal(0, code);
		Assert.Contains("users create", all);
		Assert.Contains("shutdown", all);
		Assert.Contains("api: POST /v1/users", one);
		Assert.Contains("--username", one);
		Assert.Equal(1, (await fixture.RunAsync("help nothing")).Code);
	}

	[Fact]
	public async Task Status_ShowsTheVersionUptimeAndCounts()
	{
		var (code, text) = await fixture.RunAsync("status");

		Assert.Equal(0, code);
		foreach (var label in new[] { "version:", "uptime:", "users online:", "osu connections:", "rooms:", "rounds in progress:" })
			Assert.Contains(label, text);
	}

	[Fact]
	public async Task Shutdown_WithNow_AsksNoQuestionAndRequestsTheShutdown()
	{
		var (runner, control) = RunnerWithControl();

		var (code, text) = await RunAsync(runner, "shutdown --now");

		Assert.Equal(0, code);
		Assert.Equal([false], control.Requests);
		Assert.Contains("shutting down", text);
	}

	[Fact]
	public async Task Restart_RequestsARestart()
	{
		var (runner, control) = RunnerWithControl();

		await RunAsync(runner, "restart --now");

		Assert.Equal([true], control.Requests);
	}

	[Fact]
	public async Task Shutdown_AsksForConfirmation_AndDecliningEndsWithCode4WithoutShuttingDown()
	{
		var (runner, control) = RunnerWithControl();
		string? asked = null;
		var session = new CommandSession(true, (question, _) =>
		{
			asked = question;
			return Task.FromResult(false);
		});

		var (code, text) = await RunAsync(runner, "shutdown", session: session);

		Assert.Equal(4, code);
		Assert.Equal("Really shut down the server?", asked);
		Assert.Equal("Cancelled.", text);
		Assert.Empty(control.Requests);
	}

	[Fact]
	public async Task Shutdown_Confirmed_ShutsDown()
	{
		var (runner, control) = RunnerWithControl();

		var (code, _) = await RunAsync(runner, "shutdown", session: new CommandSession(true, (_, _) => Task.FromResult(true)));

		Assert.Equal(0, code);
		Assert.Equal([false], control.Requests);
	}

	[Fact]
	public async Task Shutdown_WhereNobodyCanBeAsked_NeedsNow()
	{
		var (runner, control) = RunnerWithControl();

		var (code, text) = await RunAsync(runner, "shutdown");

		Assert.Equal(2, code);
		Assert.Contains("--now", text);
		Assert.Empty(control.Requests);
	}

	[Fact]
	public async Task A_command_that_needs_the_server_is_refused_with_code3_when_it_is_not_running()
	{
		var (runner, _) = RunnerWithControl();

		var (code, text) = await RunAsync(runner, "rooms list", running: false);

		Assert.Equal(3, code);
		Assert.Contains("serverNotRunning", text);
	}

	[Fact]
	public async Task Theme_IsOnlyForTheDashboard_ListsAndChangesAndRefusesUnknownNames()
	{
		var (outside, _) = await fixture.RunAsync("theme");
		var dashboard = new CommandSession(true);
		var (_, listed) = await fixture.RunAsync("theme", dashboard);
		var lines = listed.Split('\n');
		var original = lines[0]["current: ".Length..];
		var other = lines[1].Split(", ").First(name => name != original);
		var (changed, _) = await fixture.RunAsync($"theme \"{other}\"", dashboard);
		var (_, current) = await fixture.RunAsync("theme", dashboard);
		var (unknown, _) = await fixture.RunAsync("theme NoSuchTheme", dashboard);
		await fixture.RunAsync($"theme \"{original}\"", dashboard);

		Assert.Equal(2, outside);
		Assert.Contains(Basil.Host.Cli.Dashboard.ThemeStore.DefaultScheme, lines[1]);
		Assert.Equal(0, changed);
		Assert.StartsWith($"current: {other}", current);
		Assert.Equal(1, unknown);
	}

	[Fact]
	public async Task A_command_is_logged_with_its_passwords_hidden()
	{
		var log = new CapturedLog();
		var (runner, _) = RunnerWithControl(log);
		var name = CliFixture.Name("logged");

		await RunAsync(runner, $"users create --username {name} --password hunter2");
		await RunAsync(runner, $"users create -u {name}2 -p hunter3");

		Assert.Contains(log.Messages, message => message.Contains("--password ***") && message.Contains(name));
		Assert.DoesNotContain(log.Messages, message => message.Contains("hunter2") || message.Contains("hunter3"));
	}

	[Fact]
	public async Task An_unknown_command_is_a_usage_error()
	{
		var (code, text) = await fixture.RunAsync("frobnicate now");

		Assert.Equal(2, code);
		Assert.Contains("Unknown command", text);
	}
}
