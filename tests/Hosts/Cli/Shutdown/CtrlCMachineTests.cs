using Basil.Host.Cli.Shutdown;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Host.Cli.Tests.Shutdown;

/// <summary>The Ctrl+C rules of the specification, step by step, with a clock the test moves.</summary>
public sealed class CtrlCMachineTests
{
	private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

	private static (CtrlCMachine Machine, FakeTimeProvider Clock) Create(ConsoleLevel mode)
	{
		var clock = new FakeTimeProvider();
		return (new CtrlCMachine(clock, new CliOptions(mode)), clock);
	}

	[Fact]
	public void FirstPressText_ClearsLine()
	{
		var (machine, _) = Create(ConsoleLevel.Full);

		Assert.Equal(CtrlCAction.ClearLine, machine.Press(true));
		Assert.Equal(CtrlCState.Running, machine.State);
	}

	[Fact]
	public void FirstPressEmpty_WarnsOpensWindow()
	{
		var (machine, _) = Create(ConsoleLevel.Full);

		Assert.Equal(CtrlCAction.WarnConfirm, machine.Press());
		Assert.Equal(CtrlCState.Confirming, machine.State);
	}

	[Fact]
	public void NoConsoleMode_FirstPressWarns()
	{
		var (machine, _) = Create(ConsoleLevel.ViewOnly);

		Assert.Equal(CtrlCAction.WarnConfirm, machine.Press(true));
	}

	[Fact]
	public void SecondPressWindow_StartsShutdown()
	{
		var (machine, clock) = Create(ConsoleLevel.Full);
		machine.Press();
		clock.Advance(Window - TimeSpan.FromMilliseconds(1));

		Assert.Equal(CtrlCAction.BeginShutdown, machine.Press());
		Assert.Equal(CtrlCState.ShuttingDown, machine.State);
	}

	[Fact]
	public void PressAfterWindow_FirstAgain()
	{
		var (machine, clock) = Create(ConsoleLevel.Full);
		machine.Press();
		clock.Advance(Window);

		Assert.Equal(CtrlCAction.WarnConfirm, machine.Press());
		Assert.Equal(CtrlCState.Confirming, machine.State);
	}

	[Fact]
	public void WindowEnd_TickReportsRunning()
	{
		var (machine, clock) = Create(ConsoleLevel.Full);
		machine.Press();
		clock.Advance(Window);

		Assert.Equal(CtrlCAction.ConfirmExpired, machine.Tick());
		Assert.Equal(CtrlCState.Running, machine.State);
	}

	[Fact]
	public void TickInsideWindow_Nothing()
	{
		var (machine, clock) = Create(ConsoleLevel.Full);
		machine.Press();
		clock.Advance(TimeSpan.FromSeconds(1));

		Assert.Equal(CtrlCAction.None, machine.Tick());
		Assert.Equal(CtrlCState.Confirming, machine.State);
	}

	[Fact]
	public void NoGuiMode_FirstPressStops()
	{
		var (machine, _) = Create(ConsoleLevel.Log);

		Assert.Equal(CtrlCAction.BeginShutdown, machine.Press(true));
	}

	[Theory]
	[InlineData(ConsoleLevel.Full)]
	[InlineData(ConsoleLevel.ViewOnly)]
	[InlineData(ConsoleLevel.Log)]
	public void Terminate_StopsEveryMode(ConsoleLevel mode)
	{
		var (machine, _) = Create(mode);

		Assert.Equal(CtrlCAction.BeginShutdown, machine.Terminate());
	}

	[Fact]
	public void TerminateInWindow_BeginsShutdown()
	{
		var (machine, _) = Create(ConsoleLevel.Full);
		machine.Press();

		Assert.Equal(CtrlCAction.BeginShutdown, machine.Terminate());
	}

	[Fact]
	public void TerminateDuringShutdown_Nothing()
	{
		var (machine, _) = Create(ConsoleLevel.Full);
		machine.Terminate();

		Assert.Equal(CtrlCAction.None, machine.Terminate());
	}

	[Theory]
	[InlineData(ConsoleLevel.Full)]
	[InlineData(ConsoleLevel.ViewOnly)]
	[InlineData(ConsoleLevel.Log)]
	public void EarlyPresses_Ignored(ConsoleLevel mode)
	{
		var (machine, clock) = Create(mode);
		machine.Terminate();

		for (var i = 0; i < 5; i++)
		{
			clock.Advance(TimeSpan.FromSeconds(5));
			Assert.Equal(CtrlCAction.None, machine.Press());
			Assert.Equal(CtrlCAction.None, machine.Tick());
		}

		Assert.Equal(CtrlCState.ShuttingDown, machine.State);
	}

	[Fact]
	public void LateTick_WarnsThenForces()
	{
		var (machine, clock) = Create(ConsoleLevel.Full);
		machine.Terminate();
		clock.Advance(Grace);

		Assert.Equal(CtrlCAction.WarnSlow, machine.Tick());
		Assert.Equal(CtrlCAction.None, machine.Tick());
		Assert.Equal(CtrlCState.ForceWarned, machine.State);
		Assert.Equal(CtrlCAction.ForceExit, machine.Press());
	}

	[Fact]
	public void PressBeforeWarning_ShowsWarning()
	{
		var (machine, clock) = Create(ConsoleLevel.Full);
		machine.Terminate();
		clock.Advance(Grace + TimeSpan.FromSeconds(1));

		Assert.Equal(CtrlCAction.WarnSlow, machine.Press());
		Assert.Equal(CtrlCAction.ForceExit, machine.Press());
	}

	[Fact]
	public void ThirtySeconds_FromShutdownStart()
	{
		var (machine, clock) = Create(ConsoleLevel.Full);
		machine.Press();
		clock.Advance(TimeSpan.FromSeconds(2));
		machine.Press();
		clock.Advance(Grace - TimeSpan.FromSeconds(1));

		Assert.Equal(CtrlCAction.None, machine.Tick());
		clock.Advance(TimeSpan.FromSeconds(1));
		Assert.Equal(CtrlCAction.WarnSlow, machine.Tick());
	}

	[Fact]
	public void FinishedShutdown_Nothing()
	{
		var (machine, clock) = Create(ConsoleLevel.Full);
		machine.Terminate();
		machine.Finish();
		clock.Advance(Grace * 2);

		Assert.Equal(CtrlCState.Done, machine.State);
		Assert.Equal(CtrlCAction.None, machine.Press());
		Assert.Equal(CtrlCAction.None, machine.Tick());
		Assert.Equal(CtrlCAction.None, machine.Terminate());
	}

	[Fact]
	public void ConfirmationAndForce_Separate()
	{
		var (machine, clock) = Create(ConsoleLevel.Log);
		Assert.Equal(CtrlCAction.BeginShutdown, machine.Press());
		clock.Advance(TimeSpan.FromSeconds(1));
		Assert.Equal(CtrlCAction.None, machine.Press());
		clock.Advance(Grace);
		Assert.Equal(CtrlCAction.WarnSlow, machine.Tick());
		Assert.Equal(CtrlCAction.ForceExit, machine.Press());
	}
}