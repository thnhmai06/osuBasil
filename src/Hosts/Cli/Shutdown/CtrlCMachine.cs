namespace Basil.Host.Cli.Shutdown;

/// <summary>What the display has to do after a Ctrl+C or a moment passing.</summary>
public enum CtrlCAction : byte
{
	/// <summary>Nothing.</summary>
	None,

	/// <summary>Clear the line being typed.</summary>
	ClearLine,

	/// <summary>Show <c>Press Ctrl+C again within 3 seconds to shut down the server.</c></summary>
	WarnConfirm,

	/// <summary>Hide the confirmation warning: the 3 seconds are over.</summary>
	ConfirmExpired,

	/// <summary>Start the graceful shutdown and show its progress.</summary>
	BeginShutdown,

	/// <summary>
	///     Show
	///     <c>Shutdown is taking longer than expected. Press Ctrl+C again to force shutdown (data loss may occur).</c>
	/// </summary>
	WarnSlow,

	/// <summary>End the process now, skipping the remaining steps.</summary>
	ForceExit
}

/// <summary>Where the machine is.</summary>
public enum CtrlCState : byte
{
	/// <summary>Running normally.</summary>
	Running,

	/// <summary>The first Ctrl+C opened the confirmation window.</summary>
	Confirming,

	/// <summary>The graceful shutdown runs; Ctrl+C is ignored.</summary>
	ShuttingDown,

	/// <summary>The shutdown is slow and the warning was shown: the next Ctrl+C forces the exit.</summary>
	ForceWarned,

	/// <summary>The shutdown finished.</summary>
	Done
}

/// <summary>
///     The Ctrl+C rules, apart from any terminal: a first press clears the line or asks for confirmation (or shuts down
///     at once in <c>--nogui</c>), a second press within 3 seconds shuts down, and while shutting down nothing ends the
///     process for 30 seconds, after which a warning is shown and one more press forces the exit.
/// </summary>
/// <param name="clock">The clock, which tests replace.</param>
/// <param name="mode">How the server shows itself.</param>
public sealed class CtrlCMachine(TimeProvider clock, CliMode mode)
{
	/// <summary>How long the second press has to come after the first.</summary>
	public static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);

	/// <summary>How long a graceful shutdown may take before the warning appears.</summary>
	public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(30);

	private readonly Lock _gate = new();
	private DateTimeOffset _until;

	/// <summary>Gets where the machine is.</summary>
	public CtrlCState State { get; private set; }

	/// <summary>Handles a Ctrl+C.</summary>
	/// <param name="hasTypedText">Whether the command box holds text.</param>
	/// <returns>What to do.</returns>
	public CtrlCAction Press(bool hasTypedText = false)
	{
		lock (_gate)
		{
			Expire();
			switch (State)
			{
				case CtrlCState.Running:
					if (mode == CliMode.Full && hasTypedText) return CtrlCAction.ClearLine;
					if (mode == CliMode.NoGui) return Begin();

					State = CtrlCState.Confirming;
					_until = clock.GetUtcNow() + ConfirmWindow;
					return CtrlCAction.WarnConfirm;
				case CtrlCState.Confirming:
					return Begin();
				case CtrlCState.ShuttingDown:
					// The warning belongs to the moment the grace period ends; a press that arrives first shows it.
					return clock.GetUtcNow() >= _until ? Warn() : CtrlCAction.None;
				case CtrlCState.ForceWarned:
					return CtrlCAction.ForceExit;
				default:
					return CtrlCAction.None;
			}
		}
	}

	/// <summary>Handles the operating system's request to stop (SIGTERM): the graceful shutdown starts at once.</summary>
	/// <returns>What to do.</returns>
	public CtrlCAction Terminate()
	{
		lock (_gate)
		{
			Expire();
			return State is CtrlCState.Running or CtrlCState.Confirming ? Begin() : CtrlCAction.None;
		}
	}

	/// <summary>Lets time pass: ends the confirmation window and raises the slow-shutdown warning.</summary>
	/// <returns>What to do.</returns>
	public CtrlCAction Tick()
	{
		lock (_gate)
		{
			if (State == CtrlCState.Confirming && clock.GetUtcNow() >= _until)
			{
				State = CtrlCState.Running;
				return CtrlCAction.ConfirmExpired;
			}

			return State == CtrlCState.ShuttingDown && clock.GetUtcNow() >= _until ? Warn() : CtrlCAction.None;
		}
	}

	/// <summary>Tells the machine that the shutdown finished.</summary>
	public void Finish()
	{
		lock (_gate)
		{
			State = CtrlCState.Done;
		}
	}

	private CtrlCAction Begin()
	{
		State = CtrlCState.ShuttingDown;
		_until = clock.GetUtcNow() + GracePeriod;
		return CtrlCAction.BeginShutdown;
	}

	private CtrlCAction Warn()
	{
		State = CtrlCState.ForceWarned;
		return CtrlCAction.WarnSlow;
	}

	private void Expire()
	{
		if (State == CtrlCState.Confirming && clock.GetUtcNow() >= _until) State = CtrlCState.Running;
	}
}