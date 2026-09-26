namespace Basil.Application.Models;

public sealed class Countdown : IDisposable
{
	private readonly Milestone[] _milestones;
	private readonly Lock _sync = new();
	private readonly TimeProvider _timeProvider;
	private CancellationTokenSource? _cts;
	private int _nextMilestoneIndex;

	private DateTimeOffset? _startedAt;
	private ITimer? _timer;

	public Countdown(TimeSpan length, IEnumerable<Milestone> milestones, TimeProvider? timeProvider = null)
	{
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero);
		ArgumentNullException.ThrowIfNull(milestones);

		_timeProvider = timeProvider ?? TimeProvider.System;

		_milestones =
		[
			.. milestones
				.Where(x =>
					x.Remaining >= TimeSpan.Zero &&
					x.Remaining <= length)
				.OrderByDescending(x => x.Remaining)
		];

		Length = length;
	}

	public TimeSpan Length { get; }

	public DateTimeOffset? StartedAt
	{
		get
		{
			lock (_sync)
			{
				return _startedAt;
			}
		}
	}

	public bool IsStarted => StartedAt is not null;

	public DateTimeOffset? EndAt =>
		StartedAt is { } startedAt
			? startedAt + Length
			: null;

	public TimeSpan Remaining
	{
		get
		{
			if (StartedAt is not { } start)
				return Length;

			var remaining = start + Length - _timeProvider.GetUtcNow();

			return remaining > TimeSpan.Zero
				? remaining
				: TimeSpan.Zero;
		}
	}

	public void Dispose()
	{
		Reset();
	}

	public void Start()
	{
		lock (_sync)
		{
			if (_startedAt is not null)
				return;

			_cts?.Dispose();
			_cts = new CancellationTokenSource();

			_startedAt = _timeProvider.GetUtcNow();
			_nextMilestoneIndex = 0;

			ScheduleNextMilestone();
		}
	}

	public void Reset()
	{
		lock (_sync)
		{
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;

			_timer?.Dispose();
			_timer = null;

			_startedAt = null;
			_nextMilestoneIndex = 0;
		}
	}

	private void ScheduleNextMilestone()
	{
		if (_nextMilestoneIndex >= _milestones.Length)
			return;

		var cts = _cts;
		if (cts is null || cts.IsCancellationRequested)
			return;

		var milestone = _milestones[_nextMilestoneIndex];

		var delay = Remaining - milestone.Remaining;

		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;

		_timer = _timeProvider.CreateTimer(
			static state => ((Countdown)state!).OnMilestone(),
			this, delay, Timeout.InfiniteTimeSpan);
	}

	private void OnMilestone()
	{
		Milestone milestone;
		CancellationToken cancellationToken;

		lock (_sync)
		{
			if (_startedAt is null ||
			    _cts is not { IsCancellationRequested: false } cts ||
			    _nextMilestoneIndex >= _milestones.Length) return;

			milestone = _milestones[_nextMilestoneIndex++];

			cancellationToken = cts.Token;

			_timer?.Dispose();
			_timer = null;

			ScheduleNextMilestone();
		}

		_ = milestone.Execute(cancellationToken);
	}

	public readonly record struct Milestone(
		TimeSpan Remaining,
		Func<CancellationToken, Task> Action,
		Func<Exception, Task>? OnException = null)
	{
		public async Task Execute(CancellationToken ct = default)
		{
			try
			{
				await Action(ct);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
			}
			catch (Exception e)
			{
				if (OnException is not null)
					await OnException(e);
			}
		}
	}
}