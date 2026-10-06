namespace Basil.Application.Services.Multiplayer;

/// <summary>A countdown that runs one action at each of its milestones, the last when it ends.</summary>
/// <remarks>
///     It starts when created and stops when disposed. A milestone already firing when the countdown stops still runs
///     its action, so an action checks that its countdown is still the current one.
/// </remarks>
internal sealed class Countdown : IDisposable
{
	private readonly ITimer[] _timers;

	/// <summary>Starts a countdown.</summary>
	/// <param name="length">How long the countdown runs; more than zero.</param>
	/// <param name="milestones">The actions to run; those that fall outside the countdown's length are ignored.</param>
	/// <param name="timeProvider">The clock that measures the countdown.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="length" /> is not more than zero.</exception>
	public Countdown(TimeSpan length, IEnumerable<Milestone> milestones, TimeProvider timeProvider)
	{
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero);

		EndsAt = timeProvider.GetUtcNow() + length;
		_timers =
		[
			.. milestones
				.Where(milestone => milestone.Remaining >= TimeSpan.Zero && milestone.Remaining <= length)
				.Select(milestone => timeProvider.CreateTimer(
					static state =>
					{
						var (countdown, milestone) = ((Countdown, Milestone))state!;
						_ = milestone.Execute(countdown);
					},
					(this, milestone), length - milestone.Remaining, Timeout.InfiniteTimeSpan))
		];
	}

	/// <summary>Gets when the countdown ends.</summary>
	public DateTimeOffset EndsAt { get; }

	/// <summary>Stops the countdown; its milestones that have not fired do not run.</summary>
	public void Dispose()
	{
		foreach (var timer in _timers)
			timer.Dispose();
	}

	/// <summary>An action that runs when a countdown has a given time left.</summary>
	/// <param name="Remaining">The time left on the countdown when the action runs; zero runs it when the countdown ends.</param>
	/// <param name="Action">The action to run; it receives the countdown it belongs to.</param>
	public readonly record struct Milestone(TimeSpan Remaining, Func<Countdown, Task> Action)
	{
		/// <summary>Runs the action; a failure of the action is ignored.</summary>
		/// <param name="countdown">The countdown the milestone belongs to.</param>
		public async Task Execute(Countdown countdown)
		{
			try
			{
				await Action(countdown);
			}
			catch (Exception)
			{
				// A milestone must never fault the countdown that runs it.
			}
		}
	}
}
