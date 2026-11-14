using Basil.Host.Cli.Shutdown;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Host.Cli.Tests.Shutdown;

/// <summary>The Ctrl+C rules acting on the host: what is stopped, what is shown, which exit code the process gets.</summary>
public sealed class ShutdownControllerTests
{
	private static (ShutdownController Controller, Lifetime Lifetime, FakeTimeProvider Clock, List<ShutdownMessage>
		Messages, List<int> Exits) Create(ConsoleLevel mode)
	{
		var clock = new FakeTimeProvider();
		var lifetime = new Lifetime();
		var exits = new List<int>();
		var controller = new ShutdownController(new CtrlCMachine(clock, new CliOptions(mode)), lifetime,
			NullLogger<ShutdownController>.Instance, exits.Add);
		var messages = new List<ShutdownMessage>();
		controller.Message += messages.Add;
		return (controller, lifetime, clock, messages, exits);
	}

	[Fact]
	public void TwoPresses_StopOnce()
	{
		var (controller, lifetime, _, messages, _) = Create(ConsoleLevel.Full);

		controller.Interrupt();
		controller.Interrupt();
		controller.Interrupt();

		Assert.Equal(1, lifetime.Stops);
		Assert.Equal("Press Ctrl+C again within 3 seconds to shut down the server.", messages[0].Text);
		Assert.Equal(ShutdownMessageKind.Progress, messages[1].Kind);
	}

	[Fact]
	public void WindowTimeout_HidesWarning()
	{
		var (controller, lifetime, clock, messages, _) = Create(ConsoleLevel.Full);

		controller.Interrupt();
		clock.Advance(TimeSpan.FromSeconds(4));
		controller.Tick();

		Assert.Equal(0, lifetime.Stops);
		Assert.Equal(ShutdownMessageKind.Clear, messages[^1].Kind);
	}

	[Fact]
	public void SlowShutdown_WarnsThenForces()
	{
		var (controller, _, clock, messages, exits) = Create(ConsoleLevel.Log);
		controller.Interrupt();
		clock.Advance(TimeSpan.FromSeconds(31));
		controller.Tick();
		controller.Interrupt();

		Assert.Equal(
			"Shutdown is taking longer than expected. Press Ctrl+C again to force shutdown (data loss may occur).",
			messages[^1].Text);
		Assert.Equal([ProcessExitCodes.Error], exits);
	}

	[Fact]
	public void TerminateAndCommand_StopAtOnce()
	{
		var (controller, lifetime, _, _, _) = Create(ConsoleLevel.Full);

		controller.Terminate();
		controller.RequestShutdown(false);

		Assert.Equal(1, lifetime.Stops);
		Assert.Equal(ProcessExitCodes.Normal, controller.ExitCode);
	}

	[Fact]
	public void RestartCommand_ExitCodeRestart()
	{
		var (controller, lifetime, _, _, _) = Create(ConsoleLevel.Full);

		controller.RequestShutdown(true);

		Assert.Equal(1, lifetime.Stops);
		Assert.True(controller.RestartRequested);
		Assert.Equal(75, controller.ExitCode);
	}

	[Fact]
	public void FirstPressText_AsksLineClear()
	{
		var (controller, lifetime, _, messages, _) = Create(ConsoleLevel.Full);

		controller.Interrupt(true);

		Assert.Equal(ShutdownMessageKind.ClearLine, messages.Single().Kind);
		Assert.Equal(0, lifetime.Stops);
	}

	[Theory]
	[InlineData(new[] { "--console", "0" }, false, false, false, false, ConsoleLevel.Log)]
	[InlineData(new[] { "--console", "1" }, false, false, false, false, ConsoleLevel.Full)]
	[InlineData(new[] { "--console", "2" }, false, false, false, false, ConsoleLevel.ViewOnly)]
	[InlineData(new[] { "--console=2" }, false, false, false, false, ConsoleLevel.ViewOnly)]
	[InlineData(new string[0], false, false, false, false, ConsoleLevel.Full)]
	[InlineData(new string[0], true, false, false, false, ConsoleLevel.Log)]
	[InlineData(new string[0], false, true, false, false, ConsoleLevel.Log)]
	[InlineData(new string[0], false, false, true, false, ConsoleLevel.Log)]
	[InlineData(new[] { "--console", "2" }, false, true, false, false, ConsoleLevel.Log)]
	[InlineData(new string[0], false, false, false, true, ConsoleLevel.Full)]
	public void The_console_level_follows_the_arguments_and_the_terminal(string[] args, bool inRedirected,
		bool outRedirected, bool termDumb, bool noColorEnvironment, ConsoleLevel expected)
	{
		Assert.Equal(expected,
			CliModes.Choose(args, inRedirected, outRedirected, termDumb, noColorEnvironment).Console);
	}

	[Theory]
	[InlineData(new string[0], false, false)]
	[InlineData(new[] { "--nocolor" }, false, true)]
	[InlineData(new[] { "--ascii" }, true, false)]
	[InlineData(new[] { "--nocolor", "--ascii" }, true, true)]
	public void The_flags_set_ascii_and_no_colour(string[] args, bool ascii, bool noColour)
	{
		var options = CliModes.Choose(args, false, false, false, false);

		Assert.Equal(ascii, options.Ascii);
		Assert.Equal(noColour, options.NoColor);
	}

	[Fact]
	public void NoColorEnvironment_SetsNoColour_NotLogOnly()
	{
		var options = CliModes.Choose([], false, false, false, true);

		Assert.True(options.NoColor);
		Assert.True(options.HasDashboard);
	}

	[Theory]
	[InlineData("--nogui")]
	[InlineData("--noconsole")]
	[InlineData("--console")]
	[InlineData("--console=3")]
	public void RemovedFlagsAndBadValues_AreRefused(string arg)
	{
		Assert.Throws<CliOptionException>(() => CliModes.Choose([arg], false, false, false, false));
	}

	[Fact]
	public void ViewOnly_IsNotEditable()
	{
		Assert.False(new CliOptions(ConsoleLevel.ViewOnly).Editable);
		Assert.True(new CliOptions(ConsoleLevel.Full).Editable);
	}

	[Fact]
	public void DisplayArguments_NotInConfiguration()
	{
		Assert.Equal(["--urls", "http://x"],
			CliModes.Strip(["--console", "0", "--urls", "http://x", "--ascii", "--nocolor", "--console=2"]));
	}

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
}