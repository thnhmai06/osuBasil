namespace Basil.Host.Lifetime;

/// <summary>One step of the graceful shutdown.</summary>
/// <param name="Name">What the step does, as the log says it.</param>
/// <param name="Timeout">The longest the step may take; a step that takes longer is left behind and the next one starts.</param>
/// <param name="Run">Does the work; its token is cancelled when the step times out or the whole shutdown is cancelled.</param>
public sealed record ShutdownStep(string Name, TimeSpan Timeout, Func<CancellationToken, Task> Run);

/// <summary>How a step ended.</summary>
public enum ShutdownStepOutcome : byte
{
	/// <summary>The step finished in time.</summary>
	Completed,

	/// <summary>The step did not finish in time and was left behind.</summary>
	TimedOut,

	/// <summary>The step threw an exception.</summary>
	Failed,

	/// <summary>The whole shutdown was cancelled (forced) before the step finished.</summary>
	Cancelled
}

/// <summary>What happened to one step.</summary>
/// <param name="Name">The name of the step.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Elapsed">How long it took on the clock of the sequence.</param>
public sealed record ShutdownStepResult(string Name, ShutdownStepOutcome Outcome, TimeSpan Elapsed);

/// <summary>
///     Runs the steps of a graceful shutdown one after the other, each with its own time limit, so that a component that
///     hangs delays the shutdown by its limit and no more.
/// </summary>
/// <param name="clock">The clock that measures the limits.</param>
/// <param name="logger">Reports each step, its time and its failure.</param>
public sealed class ShutdownSequence(TimeProvider clock, ILogger<ShutdownSequence> logger)
{
	/// <summary>Runs the steps in order.</summary>
	/// <param name="steps">The steps.</param>
	/// <param name="cancellationToken">A token that ends the whole shutdown at once: the running step is cancelled and the rest are skipped.</param>
	/// <returns>What happened to each step that started.</returns>
	public async Task<IReadOnlyList<ShutdownStepResult>> RunAsync(IEnumerable<ShutdownStep> steps,
		CancellationToken cancellationToken)
	{
		var results = new List<ShutdownStepResult>();
		foreach (var step in steps)
		{
			var started = clock.GetTimestamp();
			var outcome = await RunStepAsync(step, cancellationToken);
			var result = new ShutdownStepResult(step.Name, outcome, clock.GetElapsedTime(started));
			results.Add(result);
			switch (outcome)
			{
				case ShutdownStepOutcome.Completed:
					logger.LogInformation("Shutdown step {Step} done in {Milliseconds} ms", step.Name, (long)result.Elapsed.TotalMilliseconds);
					break;
				case ShutdownStepOutcome.TimedOut:
					logger.LogWarning("Shutdown step {Step} did not finish within {Seconds} s and was left behind", step.Name,
						step.Timeout.TotalSeconds);
					break;
				case ShutdownStepOutcome.Cancelled:
					logger.LogWarning("Shutdown was forced during {Step}; the remaining steps are skipped", step.Name);
					return results;
			}
		}

		return results;
	}

	private async Task<ShutdownStepOutcome> RunStepAsync(ShutdownStep step, CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested) return ShutdownStepOutcome.Cancelled;

		using var stepToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		try
		{
			await step.Run(stepToken.Token).WaitAsync(step.Timeout, clock, cancellationToken);
			return ShutdownStepOutcome.Completed;
		}
		catch (TimeoutException)
		{
			await stepToken.CancelAsync();
			return ShutdownStepOutcome.TimedOut;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
