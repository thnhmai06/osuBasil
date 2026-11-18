using Basil.Infrastructure.Beatmaps.Adapters;

namespace Basil.Infrastructure.Beatmaps.Tests.Adapters;

public sealed class SingleFlightTests
{
	[Fact]
	public async Task SameKeyAtTheSameTime_RunsTheWorkOnce()
	{
		var flights = new SingleFlight<string, int>();
		var release = new TaskCompletionSource();
		var runs = 0;

		var callers = Enumerable.Range(0, 20).Select(_ => flights.RunAsync("key", async () =>
		{
			Interlocked.Increment(ref runs);
			await release.Task;
			return 7;
		}, TestContext.Current.CancellationToken)).ToList();
		release.SetResult();

		Assert.All(await Task.WhenAll(callers), value => Assert.Equal(7, value));
		Assert.Equal(1, runs);
	}

	[Fact]
	public async Task DifferentKeys_DoNotWaitForEachOther()
	{
		var flights = new SingleFlight<string, int>();
		var never = new TaskCompletionSource();

		var stuck = flights.RunAsync("slow", async () =>
		{
			await never.Task;
			return 1;
		}, TestContext.Current.CancellationToken);
		var quick = await flights.RunAsync("quick", () => Task.FromResult(2), TestContext.Current.CancellationToken)
			.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

		Assert.Equal(2, quick);
		Assert.False(stuck.IsCompleted);
		never.SetResult();
		Assert.Equal(1, await stuck);
	}

	[Fact]
	public async Task FailedRun_IsNotKept()
	{
		var flights = new SingleFlight<string, int>();
		var release = new TaskCompletionSource();
		var runs = 0;

		var waiting = Enumerable.Range(0, 3).Select(_ => flights.RunAsync("key", async () =>
		{
			Interlocked.Increment(ref runs);
			await release.Task;
			throw new InvalidOperationException("calculation failed");
		}, TestContext.Current.CancellationToken)).ToList();
		release.SetResult();

		foreach (var caller in waiting) await Assert.ThrowsAsync<InvalidOperationException>(() => caller);
		Assert.Equal(1, runs);

		var again = await flights.RunAsync("key", () => Task.FromResult(5), TestContext.Current.CancellationToken);
		Assert.Equal(5, again);
	}

	[Fact]
	public async Task CallerThatCancels_DoesNotCancelTheSharedWork()
	{
		var flights = new SingleFlight<string, int>();
		var release = new TaskCompletionSource();
		using var impatient = new CancellationTokenSource();

		var first = flights.RunAsync("key", async () =>
		{
			await release.Task;
			return 9;
		}, impatient.Token);
		var second = flights.RunAsync("key", () => Task.FromResult(0), TestContext.Current.CancellationToken);
		await impatient.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
		release.SetResult();
		Assert.Equal(9, await second);
	}
}
