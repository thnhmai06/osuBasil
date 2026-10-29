using Basil.Host.Cli.Shutdown;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Host.Cli.Tests.Shutdown;

/// <summary>The Ctrl+C rules acting on the host: what is stopped, what is shown, which exit code the process gets.</summary>
public sealed class ShutdownControllerTests
{
	private sealed class Lifetime : IHostApplicationLifetime
	{
		public int Stops { get; private set; }

		public CancellationToken ApplicationStarted => CancellationToken.None;

		public CancellationToken ApplicationStopping => CancellationToken.None;

		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
			Stops++;
		}
	}

	private static (ShutdownController Controller, Lifetime Lifetime, FakeTimeProvider Clock, List<ShutdownMessage> Messages, List<int> Exits) Create(CliMode mode)
	{
		var clock = new FakeTimeProvider();
		var lifetime = new Lifetime();
		var exits = new List<int>();
		var controller = new ShutdownController(new CtrlCMachine(clock, mode), lifetime, NullLogger<ShutdownController>.Instance, exits.Add);
		var messages = new List<ShutdownMessage>();
		controller.Message += messages.Add;
		return (controller, lifetime, clock, messages, exits);
	}

	[Fact]
	public void Two_presses_in_the_window_stop_the_application_once()
	{
		var (controller, lifetime, _, messages, _) = Create(CliMode.Full);

		controller.Interrupt();
		controller.Interrupt();
		controller.Interrupt();

		Assert.Equal(1, lifetime.Stops);
		Assert.Equal("Press Ctrl+C again within 3 seconds to shut down the server.", messages[0].Text);
		Assert.Equal(ShutdownMessageKind.Progress, messages[1].Kind);
	}

	[Fact]
	public void The_window_running_out_hides_the_warning_and_stops_nothing()
	{
		var (controller, lifetime, clock, messages, _) = Create(CliMode.Full);

		controller.Interrupt();
		clock.Advance(TimeSpan.FromSeconds(4));
		controller.Tick();

		Assert.Equal(0, lifetime.Stops);
		Assert.Equal(ShutdownMessageKind.Clear, messages[^1].Kind);
	}

	[Fact]
	public void A_slow_shutdown_warns_with_the_wording_of_the_specification_and_a_press_then_forces_exit_code_1()
	{
		var (controller, _, clock, messages, exits) = Create(CliMode.NoGui);
		controller.Interrupt();
		clock.Advance(TimeSpan.FromSeconds(31));
		controller.Tick();
		controller.Interrupt();

		Assert.Equal("Shutdown is taking longer than expected. Press Ctrl+C again to force shutdown (data loss may occur).", messages[^1].Text);
		Assert.Equal([ProcessExitCodes.Error], exits);
	}

	[Fact]
	public void Terminate_stops_at_once_and_a_command_shutdown_does_the_same()
	{
		var (controller, lifetime, _, _, _) = Create(CliMode.Full);

		controller.Terminate();
		controller.RequestShutdown(false);

		Assert.Equal(1, lifetime.Stops);
		Assert.Equal(ProcessExitCodes.Normal, controller.ExitCode);
	}

	[Fact]
	public void A_restart_command_makes_the_process_exit_with_the_restart_code()
	{
		var (controller, lifetime, _, _, _) = Create(CliMode.Full);

		controller.RequestShutdown(true);

		Assert.Equal(1, lifetime.Stops);
		Assert.True(controller.RestartRequested);
		Assert.Equal(75, controller.ExitCode);
	}

	[Fact]
	public void The_first_press_with_typed_text_asks_the_display_to_clear_the_line()
	{
		var (controller, lifetime, _, messages, _) = Create(CliMode.Full);

		controller.Interrupt(true);

		Assert.Equal(ShutdownMessageKind.ClearLine, messages.Single().Kind);
		Assert.Equal(0, lifetime.Stops);
	}

	[Theory]
	[InlineData(new[] { "--nogui" }, true, false, true, CliMode.NoGui)]
	[InlineData(new[] { "--noconsole" }, false, false, true, CliMode.NoConsole)]
	[InlineData(new[] { "--nogui", "--noconsole" }, false, false, true, CliMode.NoGui)]
	[InlineData(new string[0], false, false, true, CliMode.Full)]
	[InlineData(new string[0], true, false, true, CliMode.NoGui)]
	[InlineData(new string[0], false, true, true, CliMode.NoGui)]
	[InlineData(new string[0], false, false, false, CliMode.NoGui)]
	[InlineData(new[] { "--noconsole" }, false, true, true, CliMode.NoGui)]
	public void The_display_mode_follows_the_arguments_and_the_terminal(string[] args, bool inRedirected, bool outRedirected, bool colour, CliMode expected)
	{
		Assert.Equal(expected, CliModes.Choose(args, inRedirected, outRedirected, colour));
	}

	[Fact]
	public void The_display_arguments_are_not_left_for_the_configuration()
	{
		Assert.Equal(["--urls", "http://x"], CliModes.Strip(["--nogui", "--urls", "http://x", "--noconsole"]));
	}
}
