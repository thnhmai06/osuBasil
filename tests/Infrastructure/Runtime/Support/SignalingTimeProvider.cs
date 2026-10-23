namespace Basil.Infrastructure.Runtime.Tests.Support;

/// <summary>
///     Passes time through to another provider and reports when a timer is created, so a test can move the clock only
///     once the awaited delay is armed.
/// </summary>
/// <param name="inner">The provider that keeps the time.</param>
internal sealed class SignalingTimeProvider(TimeProvider inner) : TimeProvider
{
	private readonly TaskCompletionSource _timerCreated =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>Gets a task that completes when the first timer is created.</summary>
	public Task TimerCreated => _timerCreated.Task;

	/// <inheritdoc />
	public override DateTimeOffset GetUtcNow()
	{
		return inner.GetUtcNow();
	}

	/// <inheritdoc />
	public override long GetTimestamp()
	{
		return inner.GetTimestamp();
	}

	/// <inheritdoc />
	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		var timer = inner.CreateTimer(callback, state, dueTime, period);
		_timerCreated.TrySetResult();
		return timer;
	}
}
