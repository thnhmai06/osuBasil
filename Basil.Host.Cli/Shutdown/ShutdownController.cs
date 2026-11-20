using System.Runtime.InteropServices;
using Basil.Host.Cli.Commands;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Host.Cli.Shutdown;

public static class ProcessExitCodes
{
	public const int Normal = 0;

	public const int Error = 1;

	public const int Restart = 75;
}

public sealed class ShutdownController(
	CtrlCMachine machine,
	IHostApplicationLifetime lifetime,
	ILogger<ShutdownController> logger,
	Action<int>? exit = null)
	: IServerControl
{
	private readonly Action<int> _exit = exit ?? Environment.Exit;

	public bool RestartRequested { get; private set; }

	public int ExitCode => RestartRequested ? ProcessExitCodes.Restart : ProcessExitCodes.Normal;

	public void RequestShutdown(bool restart)
	{
		RestartRequested |= restart;
		Apply(machine.Terminate());
	}

	public event Action<ShutdownMessage>? Message;

	public void Interrupt(bool hasTypedText = false)
	{
		Apply(machine.Press(hasTypedText));
	}

	public void Terminate()
	{
		Apply(machine.Terminate());
	}

	public void Tick()
	{
		Apply(machine.Tick());
	}

	public void Finish()
	{
		machine.Finish();
	}

	private void Apply(CtrlCAction action)
	{
		switch (action)
		{
			case CtrlCAction.ClearLine:
				Message?.Invoke(new ShutdownMessage(ShutdownMessageKind.ClearLine, string.Empty));
				break;
			case CtrlCAction.WarnConfirm:
				Message?.Invoke(new ShutdownMessage(ShutdownMessageKind.Warning,
					"Press Ctrl+C again within 3 seconds to shut down the server."));
				break;
			case CtrlCAction.ConfirmExpired:
				Message?.Invoke(new ShutdownMessage(ShutdownMessageKind.Clear, string.Empty));
				break;
			case CtrlCAction.BeginShutdown:
				logger.LogInformation("Shutting down");
				Message?.Invoke(new ShutdownMessage(ShutdownMessageKind.Progress, "Shutting down the server..."));
				lifetime.StopApplication();
				break;
			case CtrlCAction.WarnSlow:
				Message?.Invoke(new ShutdownMessage(ShutdownMessageKind.Warning,
					"Shutdown is taking longer than expected. Press Ctrl+C again to force shutdown (data loss may occur)."));
				break;
			case CtrlCAction.ForceExit:
				logger.LogWarning("Shutdown forced");
				_exit(ProcessExitCodes.Error);
				break;
		}
	}
}

public enum ShutdownMessageKind : byte
{
	ClearLine,

	Warning,

	Progress,

	Clear
}

public sealed record ShutdownMessage(ShutdownMessageKind Kind, string Text);

public sealed class CliLifetime(ShutdownController controller) : IHostLifetime, IDisposable
{
	private readonly List<PosixSignalRegistration> _registrations = [];

	public void Dispose()
	{
		foreach (var registration in _registrations) registration.Dispose();
	}

	public Task WaitForStartAsync(CancellationToken ct)
	{
		_registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
		{
			context.Cancel = true;
			controller.Terminate();
		}));
		_registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
		{
			context.Cancel = true;
			controller.Interrupt();
		}));
		_registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGHUP, context =>
		{
			context.Cancel = true;
			controller.Terminate();
		}));
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken ct)
	{
		controller.Finish();
		return Task.CompletedTask;
	}
}