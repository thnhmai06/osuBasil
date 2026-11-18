using Microsoft.Extensions.Logging;

namespace Basil.Application.Shared.Events;

public static class HandlerStep
{
	public static async Task RunAsync(ILogger logger, string step, string subject, int userId, Func<Task> action,
		CancellationToken ct)
	{
		try
		{
			await action();
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !ct.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not {Step} for {Subject} {UserId}", step, subject, userId);
		}
	}

	public static void Run(ILogger logger, string step, string subject, int userId, Action action,
		CancellationToken ct)
	{
		try
		{
			action();
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !ct.IsCancellationRequested)
		{
			logger.LogError(exception, "Could not {Step} for {Subject} {UserId}", step, subject, userId);
		}
	}
}