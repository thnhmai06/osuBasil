using Microsoft.Extensions.Logging;

namespace Basil.Application.Common.Events;

/// <summary>Runs one step of an event handler so that a failure is logged and the handler's remaining steps still run.</summary>
public static class HandlerStep
{
	/// <summary>Runs an asynchronous step.</summary>
	/// <param name="logger">The logger a failure is reported to.</param>
	/// <param name="step">What the step does, as it reads after "Could not".</param>
	/// <param name="subject">Whom the step is for, as it reads before the user id.</param>
	/// <param name="userId">The id of the user the step concerns.</param>
	/// <param name="action">The step to run.</param>
	/// <param name="cancellationToken">A token that, once cancelled, lets the cancellation of the step pass unlogged.</param>
	public static async Task RunAsync(ILogger logger, string step, string subject, int userId, Func<Task> action,
		CancellationToken cancellationToken)
	{
		try
		{
			await action();
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !cancellationToken.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not {Step} for {Subject} {UserId}", step, subject, userId);
		}
	}

	/// <summary>Runs a synchronous step.</summary>
	/// <param name="logger">The logger a failure is reported to.</param>
	/// <param name="step">What the step does, as it reads after "Could not".</param>
	/// <param name="subject">Whom the step is for, as it reads before the user id.</param>
	/// <param name="userId">The id of the user the step concerns.</param>
	/// <param name="action">The step to run.</param>
	/// <param name="cancellationToken">A token that, once cancelled, lets the cancellation of the step pass unlogged.</param>
	public static void Run(ILogger logger, string step, string subject, int userId, Action action,
		CancellationToken cancellationToken)
	{
		try
		{
			action();
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !cancellationToken.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not {Step} for {Subject} {UserId}", step, subject, userId);
		}
	}
}