namespace Basil.Lifetime;

public sealed record ShutdownStep(string Name, TimeSpan Timeout, Func<CancellationToken, Task> Run);

public enum ShutdownStepOutcome : byte
{
	Completed,

	TimedOut,

	Failed,

	Cancelled
}

public sealed record ShutdownStepResult(string Name, ShutdownStepOutcome Outcome, TimeSpan Elapsed);

public sealed class ShutdownSequence(TimeProvider clock, ILogger<ShutdownSequence> logger)
{
	public async Task<IReadOnlyList<ShutdownStepResult>> RunAsync(IEnumerable<ShutdownStep> steps,
		CancellationToken ct)
	{
		var results = new List<ShutdownStepResult>();
		foreach (var step in steps)
		{
			var started = clock.GetTimestamp();
			var outcome = await RunStepAsync(step, ct);
			var result = new ShutdownStepResult(step.Name, outcome, clock.GetElapsedTime(started));
			results.Add(result);
			switch (outcome)
			{
				case ShutdownStepOutcome.Completed:
					logger.LogInformation("Shutdown step {Step} done in {Milliseconds} ms", step.Name,
						(long)result.Elapsed.TotalMilliseconds);
					break;
				case ShutdownStepOutcome.TimedOut:
					logger.LogWarning("Shutdown step {Step} did not finish within {Seconds} s and was left behind",
						step.Name,
						step.Timeout.TotalSeconds);
					break;
				case ShutdownStepOutcome.Cancelled:
					logger.LogWarning("Shutdown was forced during {Step}; the remaining steps are skipped", step.Name);
					return results;
			}
		}

		return results;
	}

	private async Task<ShutdownStepOutcome> RunStepAsync(ShutdownStep step, CancellationToken ct)
	{
		if (ct.IsCancellationRequested) return ShutdownStepOutcome.Cancelled;

		using var stepToken = CancellationTokenSource.CreateLinkedTokenSource(ct);
		try
		{
			await step.Run(stepToken.Token).WaitAsync(step.Timeout, clock, ct);
			return ShutdownStepOutcome.Completed;
		}
		catch (TimeoutException)
		{
			await stepToken.CancelAsync();
			return ShutdownStepOutcome.TimedOut;
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			return ShutdownStepOutcome.Cancelled;
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Shutdown step {Step} failed", step.Name);
			return ShutdownStepOutcome.Failed;
		}
	}
}