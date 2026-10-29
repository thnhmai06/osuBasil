using System.Runtime.InteropServices;
using Basil.Host.Cli.Commands;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Host.Cli.Shutdown;

/// <summary>The exit codes of the server process.</summary>
public static class ProcessExitCodes
{
	/// <summary>The server shut down normally.</summary>
	public const int Normal = 0;

	/// <summary>The server stopped because of an error.</summary>
	public const int Error = 1;

	/// <summary>The server asks its manager to start it again.</summary>
	public const int Restart = 75;
}

/// <summary>
///     Connects the Ctrl+C rules to the host: starts the graceful shutdown, tells the display what to warn about, and
///     remembers whether the shutdown is a restart.
/// </summary>
public sealed class ShutdownController : IServerControl
{
	private readonly CtrlCMachine _machine;
	private readonly IHostApplicationLifetime _lifetime;
	private readonly Action<int> _exit;
	private readonly ILogger<ShutdownController> _logger;

	/// <summary>Creates the controller.</summary>
	/// <param name="machine">The Ctrl+C rules.</param>
	/// <param name="lifetime">The lifetime of the host, which stops the application.</param>
	/// <param name="logger">Where the progress is logged.</param>
	/// <param name="exit">Ends the process at once; the tests replace it.</param>
	public ShutdownController(CtrlCMachine machine, IHostApplicationLifetime lifetime, ILogger<ShutdownController> logger,
		Action<int>? exit = null)
	{
		_machine = machine;
		_lifetime = lifetime;
		_logger = logger;
		_exit = exit ?? Environment.Exit;
	}

	/// <summary>Occurs when the display has to show or hide a message.</summary>
	public event Action<ShutdownMessage>? Message;

	/// <summary>Gets a value that indicates whether the shutdown is a restart.</summary>
	public bool RestartRequested { get; private set; }

	/// <summary>Gets the exit code of the process.</summary>
	public int ExitCode => RestartRequested ? ProcessExitCodes.Restart : ProcessExitCodes.Normal;

	/// <summary>Handles a Ctrl+C.</summary>
	/// <param name="hasTypedText">Whether the command box holds text.</param>
	public void Interrupt(bool hasTypedText = false)
	{
		Apply(_machine.Press(hasTypedText));
	}

	/// <summary>Handles the operating system's request to stop.</summary>
	public void Terminate()
	{
		Apply(_machine.Terminate());
	}

	/// <summary>Lets time pass for the confirmation window and the slow-shutdown warning.</summary>
	public void Tick()
	{
		Apply(_machine.Tick());
	}

	/// <inheritdoc />
	public void RequestShutdown(bool restart)
	{
		RestartRequested |= restart;
		Apply(_machine.Terminate());
	}

	/// <summary>Tells the controller that the shutdown finished.</summary>
	public void Finish()
	{
		_machine.Finish();
	}

	private void Apply(CtrlCAction action)
	{
		switch (action)
		{
			case CtrlCAction.ClearLine:
				Message?.Invoke(new ShutdownMessage(ShutdownMessageKind.ClearLine, string.Empty));
				break;
			case CtrlCAction.WarnConfirm:
				Message?.Invoke(new ShutdownMessage(ShutdownMessageKind.Warning, "Press Ctrl+C again within 3 seconds to shut down the server."));
				break;
			case CtrlCAction.ConfirmExpired:
				Message?.Invoke(new ShutdownMessage(ShutdownMessageKind.Clear, string.Empty));
				break;
			case CtrlCAction.BeginShutdown:
				_logger.LogInformation("Shutting down");
				Message?.Invoke(new ShutdownMessage(ShutdownMessageKind.Progress, "Shutting down the server..."));
				_lifetime.StopApplication();
				break;
			case CtrlCAction.WarnSlow:
				Message?.Invoke(new ShutdownMessage(ShutdownMessageKind.Warning,
					"Shutdown is taking longer than expected. Press Ctrl+C again to force shutdown (data loss may occur)."));
				break;
			case CtrlCAction.ForceExit:
				_logger.LogWarning("Shutdown forced");
				_exit(ProcessExitCodes.Error);
				break;
		}
	}
}

/// <summary>What the display shows after a Ctrl+C.</summary>
public enum ShutdownMessageKind : byte
{
	/// <summary>Clear the line being typed.</summary>
	ClearLine,

	/// <summary>Show a warning.</summary>
	Warning,

	/// <summary>Show the progress of the shutdown.</summary>
	Progress,

	/// <summary>Hide the warning.</summary>
	Clear
}

/// <summary>A message for the display.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Text">The text.</param>
public sealed record ShutdownMessage(ShutdownMessageKind Kind, string Text);

/// <summary>
///     The host's lifetime for the console: the operating system's request to stop (SIGTERM) starts the graceful shutdown
///     at once, and SIGINT goes through the Ctrl+C rules instead of ending the host.
/// </summary>
/// <param name="controller">The shutdown controller.</param>
public sealed class CliLifetime(ShutdownController controller) : IHostLifetime, IDisposable
{
	private readonly List<PosixSignalRegistration> _registrations = [];

	/// <inheritdoc />
	public Task WaitForStartAsync(CancellationToken cancellationToken)
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

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken)
	{
		controller.Finish();
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		foreach (var registration in _registrations) registration.Dispose();
	}
}
