using Basil.Host.Lifetime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Host.Tests.Lifetime;

/// <summary>
///     The graceful shutdown runs its steps one after the other, in order, and each step has a time limit, so a component
///     that hangs delays the shutdown by its limit and no more. The clock is a fake one: nothing here sleeps.
/// </summary>
public sealed class ShutdownSequenceTests
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);
	private readonly FakeTimeProvider _clock = new();
	private readonly List<string> _log = [];

	private ShutdownSequence Sequence => new(_clock, NullLogger<ShutdownSequence>.Instance);

	private ShutdownStep Quick(string name)
	{
		return new ShutdownStep(name, Limit, _ =>
		{
			_log.Add(name);
			return Task.CompletedTask;
		});
	}

	[Fact]
	public async Task TheStepsRunInTheOrderTheyAreGiven()
	{
		var results = await Sequence.RunAsync([Quick("first"), Quick("second"), Quick("third")],
			TestContext.Current.CancellationToken);

		Assert.Equal(["first", "second", "third"], _log);
		Assert.All(results, result => Assert.Equal(ShutdownStepOutcome.Completed, result.Outcome));
	}

	[Fact]
	public async Task AStepStartsOnlyAfterThePreviousOneEnded()
	{
		var gate = new TaskCompletionSource();
		var slow = new ShutdownStep("slow", Limit, async _ =>
		{
			_log.Add("slow started");
			await gate.Task;
			_log.Add("slow ended");
		});

		var running = Sequence.RunAsync([slow, Quick("next")], TestContext.Current.CancellationToken);
		Assert.Equal(["slow started"], _log);
		gate.SetResult();
		await running;

		Assert.Equal(["slow started", "slow ended", "next"], _log);
	}

	[Fact]
	public async Task AStepThatHangsIsLeftBehindAfterItsLimit_AndTheNextOneStillRuns()
	{
		var hung = new ShutdownStep("hung", Limit, _ => new TaskCompletionSource().Task);

		var running = Sequence.RunAsync([hung, Quick("after")], TestContext.Current.CancellationToken);
		Assert.False(running.IsCompleted);
		_clock.Advance(Limit);
		var results = await running;

		Assert.Equal(["after"], _log);
		Assert.Equal([ShutdownStepOutcome.TimedOut, ShutdownStepOutcome.Completed],
			results.Select(result => result.Outcome));
	}

	[Fact]
	public async Task AHungStepIsGivenItsOwnLimitNotMore()
	{
		var hung = new ShutdownStep("hung", Limit, _ => new TaskCompletionSource().Task);

		var running = Sequence.RunAsync([hung], TestContext.Current.CancellationToken);
		_clock.Advance(Limit - TimeSpan.FromMilliseconds(1));
		Assert.False(running.IsCompleted);
		_clock.Advance(TimeSpan.FromMilliseconds(1));
		await running;
	}

	[Fact]
	public async Task TheTokenOfAStepThatTimedOutIsCancelled()
	{
		CancellationToken seen = default;
		var hung = new ShutdownStep("hung", Limit, token =>
		{
			seen = token;
			return new TaskCompletionSource().Task;
		});

		var running = Sequence.RunAsync([hung], TestContext.Current.CancellationToken);
		_clock.Advance(Limit);
		await running;

		Assert.True(seen.IsCancellationRequested);
	}

	[Fact]
	public async Task AStepThatThrowsDoesNotStopTheShutdown()
	{
		var failing = new ShutdownStep("failing", Limit, _ => throw new InvalidOperationException("boom"));

		var results = await Sequence.RunAsync([failing, Quick("after")], TestContext.Current.CancellationToken);

		Assert.Equal(["after"], _log);
		Assert.Equal([ShutdownStepOutcome.Failed, ShutdownStepOutcome.Completed],
			results.Select(result => result.Outcome));
	}

	[Fact]
	public async Task ForcingTheShutdownCancelsTheRunningStepAndSkipsTheRest()
	{
		using var force = new CancellationTokenSource();
		var waiting = new ShutdownStep("waiting", Limit, token => Task.Delay(Timeout.Infinite, token));

		var running = Sequence.RunAsync([waiting, Quick("skipped")], force.Token);
		await force.CancelAsync();
		var results = await running;

		Assert.Empty(_log);
		Assert.Equal(ShutdownStepOutcome.Cancelled, Assert.Single(results).Outcome);
	}

	[Fact]
	public async Task EachResultSaysHowLongTheStepTookOnTheClock()
	{
		var step = new ShutdownStep("waits", Limit,
			async token => await Task.Delay(TimeSpan.FromSeconds(3), _clock, token));

		var running = Sequence.RunAsync([step], TestContext.Current.CancellationToken);
		_clock.Advance(TimeSpan.FromSeconds(3));
		var results = await running;

		Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(results).Elapsed);
	}
}