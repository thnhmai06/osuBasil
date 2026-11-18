namespace Basil.Host.Cli.Shutdown;

public enum CtrlCAction : byte
{
	None,

	ClearLine,

	WarnConfirm,

	ConfirmExpired,

	BeginShutdown,

	WarnSlow,

	ForceExit
}

public enum CtrlCState : byte
{
	Running,

	Confirming,

	ShuttingDown,

	ForceWarned,

	Done
}

public sealed class CtrlCMachine(TimeProvider clock, CliOptions mode)
{
	public static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);

	public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(30);

	private readonly Lock _gate = new();
	private DateTimeOffset _until;

	public CtrlCState State { get; private set; }

	public CtrlCAction Press(bool hasTypedText = false)
	{
		lock (_gate)
		{
			Expire();
			switch (State)
			{
				case CtrlCState.Running:
					if (mode.Console == ConsoleLevel.Full && hasTypedText) return CtrlCAction.ClearLine;
					if (mode.Console == ConsoleLevel.Log) return Begin();

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

	public CtrlCAction Terminate()
	{
		lock (_gate)
		{
			Expire();
			return State is CtrlCState.Running or CtrlCState.Confirming ? Begin() : CtrlCAction.None;
		}
	}

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