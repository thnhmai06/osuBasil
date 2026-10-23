using Basil.Bot.Chat;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Bot.Tests.Chat;

/// <summary>
///     How the queue that runs chat commands behaves: commands keep their order, one user cannot fill the queue,
///     the queue is bounded, a failing command does not stop the commands after it and a command that outlives
///     <see cref="CommandWorker.CommandTimeout" /> is cancelled.
/// </summary>
public sealed class CommandWorkerTests : IAsyncLifetime
{
	private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(10);

	private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly FakeTimeProvider _time = new();
	private readonly CommandWorker _worker;

	public CommandWorkerTests()
	{
		_worker = new CommandWorker(NullLogger<CommandWorker>.Instance, _time);
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public async ValueTask InitializeAsync()
	{
		await _worker.StartAsync(Token);
	}

	public async ValueTask DisposeAsync()
	{
		_gate.TrySetResult();
		await _worker.StopAsync(CancellationToken.None);
		_worker.Dispose();
	}

	[Fact]
	public async Task Enqueue_WithRoomToSpare_AcceptsTheCommandAndRunsIt()
	{
		var ran = NewSignal();

		var accepted = _worker.Enqueue(1, Signal(ran));

		Assert.True(accepted);
		await ran.Task.WaitAsync(HangGuard, Token);
	}

	[Fact]
	public async Task Commands_RunInTheOrderTheyWereEnqueued()
	{
		List<int> order = [];
		var last = NewSignal();

		Assert.True(_worker.Enqueue(1, _ => Record(order, 1)));
		Assert.True(_worker.Enqueue(2, _ => Record(order, 2)));
		Assert.True(_worker.Enqueue(1, async _ =>
		{
			await Record(order, 3);
			last.SetResult();
		}));
		await last.Task.WaitAsync(HangGuard, Token);

		Assert.Equal([1, 2, 3], order);
	}

	[Fact]
	public async Task Enqueue_WhenTheUserAlreadyHasTheMostCommandsWaiting_DropsTheNextOne()
	{
		await FillUserAsync(1);

		var accepted = _worker.Enqueue(1, _ => Task.CompletedTask);

		Assert.False(accepted);
	}

	[Fact]
	public async Task Enqueue_ADroppedCommand_NeverRuns()
	{
		await FillUserAsync(1);
		var droppedRan = false;
		_worker.Enqueue(1, _ =>
		{
			droppedRan = true;
			return Task.CompletedTask;
		});

		_gate.SetResult();
		await DrainAsync();

		Assert.False(droppedRan);
	}

	[Fact]
	public async Task Enqueue_WhenOneUserIsAtTheLimit_StillAcceptsAnotherUsersCommand()
	{
		await FillUserAsync(1);

		var accepted = _worker.Enqueue(2, _ => Task.CompletedTask);

		Assert.True(accepted);
	}

	[Fact]
	public async Task Enqueue_AfterTheUsersCommandsFinished_AcceptsTheirNextCommand()
	{
		await FillUserAsync(1);
		_gate.SetResult();
		await DrainAsync();
		var ran = NewSignal();

		var accepted = _worker.Enqueue(1, Signal(ran));

		Assert.True(accepted);
		await ran.Task.WaitAsync(HangGuard, Token);
	}

	[Fact]
	public async Task Commands_AfterOneThatFails_StillRun()
	{
		var ran = NewSignal();

		Assert.True(_worker.Enqueue(1, _ => Task.FromException(new InvalidOperationException())));
		Assert.True(_worker.Enqueue(1, Signal(ran)));

		await ran.Task.WaitAsync(HangGuard, Token);
	}

	[Fact]
	public async Task Enqueue_AfterTheUsersCommandFailed_AcceptsTheirNextCommands()
	{
		await FillUserAsync(1, _ => Task.FromException(new InvalidOperationException()));
		_gate.SetResult();
		await DrainAsync();
		var ran = NewSignal();

		var accepted = _worker.Enqueue(1, Signal(ran));

		Assert.True(accepted);
		await ran.Task.WaitAsync(HangGuard, Token);
	}

	[Fact]
	public async Task Enqueue_WhenTheQueueIsFull_DropsTheNextCommandEvenFromAnotherUser()
	{
		await BlockWorkerAsync(0);
		for (var user = 1; user <= CommandWorker.QueueCapacity; user++)
			Assert.True(_worker.Enqueue(user, _ => Task.CompletedTask));

		var accepted = _worker.Enqueue(CommandWorker.QueueCapacity + 1, _ => Task.CompletedTask);

		Assert.False(accepted);
	}

	[Fact]
	public async Task Enqueue_WhenTheQueueIsFull_AcceptsCommandsAgainOnceTheyRan()
	{
		await BlockWorkerAsync(0);
		var lastRan = NewSignal();
		for (var user = 1; user < CommandWorker.QueueCapacity; user++)
			Assert.True(_worker.Enqueue(user, _ => Task.CompletedTask));
		Assert.True(_worker.Enqueue(CommandWorker.QueueCapacity, Signal(lastRan)));
		Assert.False(_worker.Enqueue(CommandWorker.QueueCapacity + 1, _ => Task.CompletedTask));
		_gate.SetResult();
		await lastRan.Task.WaitAsync(HangGuard, Token);
		var ran = NewSignal();

		var accepted = _worker.Enqueue(CommandWorker.QueueCapacity + 1, Signal(ran));

		Assert.True(accepted);
		await ran.Task.WaitAsync(HangGuard, Token);
	}

	[Fact]
	public async Task A_command_that_runs_for_the_whole_timeout_is_cancelled()
	{
		var started = NewSignal();
		var cancelled = NewSignal();
		Assert.True(_worker.Enqueue(1, async cancellationToken =>
		{
			started.SetResult();
			try
			{
				await Task.Delay(Timeout.Infinite, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				cancelled.SetResult();
			}
		}));
		await started.Task.WaitAsync(HangGuard, Token);

		_time.Advance(CommandWorker.CommandTimeout);

		await cancelled.Task.WaitAsync(HangGuard, Token);
	}

	[Fact]
	public async Task A_command_is_not_cancelled_before_the_timeout()
	{
		var started = NewSignal();
		CancellationToken seen = default;
		Assert.True(_worker.Enqueue(1, async cancellationToken =>
		{
			seen = cancellationToken;
			started.SetResult();
			await _gate.Task.WaitAsync(HangGuard);
		}));
		await started.Task.WaitAsync(HangGuard, Token);

		_time.Advance(CommandWorker.CommandTimeout - TimeSpan.FromMilliseconds(1));

		Assert.False(seen.IsCancellationRequested);
	}

	[Fact]
	public async Task The_commands_after_one_that_timed_out_still_run()
	{
		var started = NewSignal();
		Assert.True(_worker.Enqueue(1, async cancellationToken =>
		{
			started.SetResult();
			await Task.Delay(Timeout.Infinite, cancellationToken);
		}));
		var ran = NewSignal();
		Assert.True(_worker.Enqueue(2, Signal(ran)));
		await started.Task.WaitAsync(HangGuard, Token);

		_time.Advance(CommandWorker.CommandTimeout);

		await ran.Task.WaitAsync(HangGuard, Token);
	}

	[Fact]
	public async Task A_user_whose_command_timed_out_can_enqueue_again()
	{
		var started = NewSignal();
		Assert.True(_worker.Enqueue(1, async cancellationToken =>
		{
			started.SetResult();
			await Task.Delay(Timeout.Infinite, cancellationToken);
		}));
		await started.Task.WaitAsync(HangGuard, Token);
		_time.Advance(CommandWorker.CommandTimeout);
		await DrainAsync();

		for (var count = 0; count < CommandWorker.PendingPerUser; count++)
			Assert.True(_worker.Enqueue(1, _ => Task.CompletedTask));
	}

	/// <summary>Gives a user the most commands they may have waiting, the first of which holds the worker until the gate opens.</summary>
	private async Task FillUserAsync(int userId, Func<CancellationToken, Task>? rest = null)
	{
		await BlockWorkerAsync(userId);
		for (var count = 1; count < CommandWorker.PendingPerUser; count++)
			Assert.True(_worker.Enqueue(userId, rest ?? (_ => Task.CompletedTask)));
	}

	/// <summary>Has a user's command take the worker and hold it until the gate opens.</summary>
	private async Task BlockWorkerAsync(int userId)
	{
		var started = NewSignal();
		Assert.True(_worker.Enqueue(userId, async cancellationToken =>
		{
			started.SetResult();
			await _gate.Task.WaitAsync(cancellationToken);
		}));
		await started.Task.WaitAsync(HangGuard, Token);
	}

	/// <summary>Waits until every command enqueued so far has run, by queueing one more behind them.</summary>
	private async Task DrainAsync()
	{
		var done = NewSignal();
		Assert.True(_worker.Enqueue(int.MaxValue, Signal(done)));
		await done.Task.WaitAsync(HangGuard, Token);
	}

	private static TaskCompletionSource NewSignal()
	{
		return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private static Func<CancellationToken, Task> Signal(TaskCompletionSource signal)
	{
		return _ =>
		{
			signal.TrySetResult();
			return Task.CompletedTask;
		};
	}

	private static Task Record(List<int> order, int value)
	{
		order.Add(value);
		return Task.CompletedTask;
	}
}