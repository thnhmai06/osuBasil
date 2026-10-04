namespace Basil.Application.Services.Multiplayer;

/// <summary>A countdown that runs one action at each of its milestones, the last when it ends.</summary>
internal sealed class Countdown : IDisposable
{
	private readonly Milestone[] _milestones;
	private readonly Lock _sync = new();
	private readonly TimeProvider _timeProvider;
	private CancellationTokenSource? _cts;
	private int _nextMilestoneIndex;

	private DateTimeOffset? _startedAt;
	private ITimer? _timer;

	/// <summary>Creates a countdown that has not started.</summary>
	/// <param name="length">How long the countdown runs; more than zero.</param>
	/// <param name="milestones">The actions to run; those that fall outside the countdown's length are ignored.</param>
	/// <param name="timeProvider">The clock that measures the countdown.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="length" /> is not more than zero.</exception>
	public Countdown(TimeSpan length, IEnumerable<Milestone> milestones, TimeProvider timeProvider)
	{
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero);
		ArgumentNullException.ThrowIfNull(milestones);

		_timeProvider = timeProvider;

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

	/// <summary>Gets how long the countdown runs.</summary>
	public TimeSpan Length { get; }

	/// <summary>Gets when the countdown ends, or <see langword="null" /> when it has not started.</summary>
	public DateTimeOffset? EndsAt
	{
		get
		{
			lock (_sync)
			{
				return _startedAt is { } startedAt ? startedAt + Length : null;
			}
		}
	}

	/// <summary>Gets how much time is left, which is the whole length before the countdown starts.</summary>
	public TimeSpan Remaining
	{
		get
		{
			if (EndsAt is not { } endsAt)
				return Length;

			var remaining = endsAt - _timeProvider.GetUtcNow();

			return remaining > TimeSpan.Zero
				? remaining
				: TimeSpan.Zero;
		}
	}

	/// <summary>Stops the countdown; its remaining milestones do not run.</summary>
	public void Dispose()
	{
		Reset();
	}

	/// <summary>Starts the countdown; starting a countdown that already runs does nothing.</summary>
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

	/// <summary>Stops the countdown and returns it to its unstarted state; its remaining milestones do not run.</summary>
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

	/// <summary>An action that runs when a countdown has a given time left.</summary>
	/// <param name="Remaining">The time left on the countdown when the action runs; zero runs it when the countdown ends.</param>
	/// <param name="Action">The action to run; it receives a token that is cancelled when the countdown stops.</param>
	public readonly record struct Milestone(
		TimeSpan Remaining,
		Func<CancellationToken, Task> Action)
	{
		/// <summary>Runs the action; a failure of the action, and its cancellation when the countdown stops, are ignored.</summary>
		/// <param name="ct">A token that is cancelled when the countdown stops.</param>
		public async Task Execute(CancellationToken ct = default)
		{
			try
			{
				await Action(ct);
			}
			catch (Exception)
			{
				// A milestone must never fault the countdown that runs it.
			}
		}
	}
}