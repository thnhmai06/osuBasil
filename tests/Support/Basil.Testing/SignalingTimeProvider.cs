namespace Basil.Testing;

public sealed class SignalingTimeProvider(TimeProvider inner) : TimeProvider
{
	private readonly TaskCompletionSource _timerCreated =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	public Task TimerCreated => _timerCreated.Task;

	public override DateTimeOffset GetUtcNow()
	{
		return inner.GetUtcNow();
	}

	public override long GetTimestamp()
	{
		return inner.GetTimestamp();
	}

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		var timer = inner.CreateTimer(callback, state, dueTime, period);
		_timerCreated.TrySetResult();
		return timer;
	}
}