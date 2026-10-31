using Basil.Host.Cli.Shutdown;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Host.Cli.Tests.Shutdown;

/// <summary>The Ctrl+C rules of the specification, step by step, with a clock the test moves.</summary>
public sealed class CtrlCMachineTests
{
	private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

	private static (CtrlCMachine Machine, FakeTimeProvider Clock) Create(CliMode mode)
	{
		var clock = new FakeTimeProvider();
		return (new CtrlCMachine(clock, mode), clock);
	}

	[Fact]
	public void First_press_with_typed_text_clears_the_line_in_the_full_mode()
	{
		var (machine, _) = Create(CliMode.Full);

		Assert.Equal(CtrlCAction.ClearLine, machine.Press(true));
		Assert.Equal(CtrlCState.Running, machine.State);
	}

	[Fact]
	public void First_press_with_nothing_typed_warns_and_opens_the_window()
	{
		var (machine, _) = Create(CliMode.Full);

		Assert.Equal(CtrlCAction.WarnConfirm, machine.Press());
		Assert.Equal(CtrlCState.Confirming, machine.State);
	}

	[Fact]
	public void No_console_mode_has_no_line_to_clear_so_the_first_press_always_warns()
	{
		var (machine, _) = Create(CliMode.NoConsole);

		Assert.Equal(CtrlCAction.WarnConfirm, machine.Press(true));
	}

	[Fact]
	public void Second_press_within_three_seconds_begins_the_shutdown()
	{
		var (machine, clock) = Create(CliMode.Full);
		machine.Press();
		clock.Advance(Window - TimeSpan.FromMilliseconds(1));

		Assert.Equal(CtrlCAction.BeginShutdown, machine.Press());
		Assert.Equal(CtrlCState.ShuttingDown, machine.State);
	}

	[Fact]
	public void Second_press_after_the_window_is_a_first_press_again()
	{
		var (machine, clock) = Create(CliMode.Full);
		machine.Press();
		clock.Advance(Window);

		Assert.Equal(CtrlCAction.WarnConfirm, machine.Press());
		Assert.Equal(CtrlCState.Confirming, machine.State);
	}

	[Fact]
	public void The_window_ending_is_reported_by_a_tick_and_returns_to_running()
	{
		var (machine, clock) = Create(CliMode.Full);
		machine.Press();
		clock.Advance(Window);

		Assert.Equal(CtrlCAction.ConfirmExpired, machine.Tick());
		Assert.Equal(CtrlCState.Running, machine.State);
	}

	[Fact]
	public void A_tick_inside_the_window_does_nothing()
	{
		var (machine, clock) = Create(CliMode.Full);
		machine.Press();
		clock.Advance(TimeSpan.FromSeconds(1));

		Assert.Equal(CtrlCAction.None, machine.Tick());
		Assert.Equal(CtrlCState.Confirming, machine.State);
	}

	[Fact]
	public void No_gui_mode_begins_the_shutdown_on_the_first_press()
	{
		var (machine, _) = Create(CliMode.NoGui);

		Assert.Equal(CtrlCAction.BeginShutdown, machine.Press(true));
	}

	[Theory]
	[InlineData(CliMode.Full)]
	[InlineData(CliMode.NoConsole)]
	[InlineData(CliMode.NoGui)]
	public void Terminate_begins_the_shutdown_at_once_in_every_mode(CliMode mode)
	{
		var (machine, _) = Create(mode);

		Assert.Equal(CtrlCAction.BeginShutdown, machine.Terminate());
	}

	[Fact]
	public void Terminate_during_the_confirmation_window_begins_the_shutdown()
	{
		var (machine, _) = Create(CliMode.Full);
		machine.Press();

		Assert.Equal(CtrlCAction.BeginShutdown, machine.Terminate());
	}

	[Fact]
	public void Terminate_during_a_shutdown_does_nothing()
	{
		var (machine, _) = Create(CliMode.Full);
		machine.Terminate();

		Assert.Equal(CtrlCAction.None, machine.Terminate());
	}

	[Theory]
	[InlineData(CliMode.Full)]
	[InlineData(CliMode.NoConsole)]
	[InlineData(CliMode.NoGui)]
	public void Presses_in_the_first_thirty_seconds_of_a_shutdown_are_ignored(CliMode mode)
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
	public void After_thirty_seconds_a_tick_warns_once_and_the_next_press_forces_the_exit()
	{
		var (machine, clock) = Create(CliMode.Full);
		machine.Terminate();
		clock.Advance(Grace);

		Assert.Equal(CtrlCAction.WarnSlow, machine.Tick());
		Assert.Equal(CtrlCAction.None, machine.Tick());
		Assert.Equal(CtrlCState.ForceWarned, machine.State);
		Assert.Equal(CtrlCAction.ForceExit, machine.Press());
	}

	[Fact]
	public void A_press_after_thirty_seconds_that_comes_before_the_warning_shows_the_warning_instead_of_forcing()
	{
		var (machine, clock) = Create(CliMode.Full);
		machine.Terminate();
		clock.Advance(Grace + TimeSpan.FromSeconds(1));

		Assert.Equal(CtrlCAction.WarnSlow, machine.Press());
		Assert.Equal(CtrlCAction.ForceExit, machine.Press());
	}

	[Fact]
	public void Thirty_seconds_are_counted_from_the_start_of_the_shutdown_not_from_the_first_press()
	{
		var (machine, clock) = Create(CliMode.Full);
		machine.Press();
		clock.Advance(TimeSpan.FromSeconds(2));
		machine.Press();
		clock.Advance(Grace - TimeSpan.FromSeconds(1));

		Assert.Equal(CtrlCAction.None, machine.Tick());
		clock.Advance(TimeSpan.FromSeconds(1));
		Assert.Equal(CtrlCAction.WarnSlow, machine.Tick());
	}

	[Fact]
	public void After_the_shutdown_finished_nothing_happens()
	{
		var (machine, clock) = Create(CliMode.Full);
		machine.Terminate();
		machine.Finish();
		clock.Advance(Grace * 2);

		Assert.Equal(CtrlCState.Done, machine.State);
		Assert.Equal(CtrlCAction.None, machine.Press());
		Assert.Equal(CtrlCAction.None, machine.Tick());
		Assert.Equal(CtrlCAction.None, machine.Terminate());
	}

	[Fact]
	public void The_confirmation_and_the_force_stages_are_separate()
	{
		var (machine, clock) = Create(CliMode.NoGui);
		Assert.Equal(CtrlCAction.BeginShutdown, machine.Press());
		clock.Advance(TimeSpan.FromSeconds(1));
		Assert.Equal(CtrlCAction.None, machine.Press());
		clock.Advance(Grace);
		Assert.Equal(CtrlCAction.WarnSlow, machine.Tick());
		Assert.Equal(CtrlCAction.ForceExit, machine.Press());
	}
}