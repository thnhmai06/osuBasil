using Basil.Application.Diagnostics.Services;
using Basil.Infrastructure.Diagnostics.Services;

namespace Basil.Infrastructure.Diagnostics.Tests.Services;

/// <summary>Each watcher gets the newest sample; one that falls behind skips older samples and never delays another.</summary>
public sealed class DiagnosticsFeedTests
{
	private static DiagnosticsSample Sample(int number)
	{
		return new DiagnosticsSample(
		[
			new KeyValuePair<string, object>("gc", number),
			new KeyValuePair<string, object>("process", number * 10)
		]);
	}

	private static async Task<DiagnosticsSample> NextAsync(IAsyncEnumerator<DiagnosticsSample> watcher)
	{
		Assert.True(await watcher.MoveNextAsync());
		return watcher.Current;
	}

	[Fact]
	public async Task SeveralSamples_NewestOnly()
	{
		var feed = new DiagnosticsFeed();
		await using var watcher = feed.Watch(null, TestContext.Current.CancellationToken)
			.GetAsyncEnumerator(TestContext.Current.CancellationToken);

		feed.Publish(Sample(1));
		feed.Publish(Sample(2));
		feed.Publish(Sample(3));

		Assert.Equal(3, (await NextAsync(watcher))["gc"]);
	}

	[Fact]
	public async Task WatchStart_NothingOlder()
	{
		var feed = new DiagnosticsFeed();
		feed.Publish(Sample(1));
		await using var watcher = feed.Watch(null, TestContext.Current.CancellationToken)
			.GetAsyncEnumerator(TestContext.Current.CancellationToken);

		feed.Publish(Sample(2));

		Assert.Equal(2, (await NextAsync(watcher))["gc"]);
	}

	[Fact]
	public async Task SlowWatcher_FastUnaffected()
	{
		var feed = new DiagnosticsFeed();
		await using var fast = feed.Watch(null, TestContext.Current.CancellationToken)
			.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		await using var slow = feed.Watch(null, TestContext.Current.CancellationToken)
			.GetAsyncEnumerator(TestContext.Current.CancellationToken);

		var seenByFast = new List<object>();
		for (var number = 1; number <= 5; number++)
		{
			feed.Publish(Sample(number));
			seenByFast.Add((await NextAsync(fast))["gc"]);
		}

		Assert.Equal(new object[] { 1, 2, 3, 4, 5 }, seenByFast);
		Assert.Equal(5, (await NextAsync(slow))["gc"]);
	}

	[Fact]
	public async Task OneCategory_OnlyThatCategory()
	{
		var feed = new DiagnosticsFeed();
		await using var watcher = feed.Watch("gc", TestContext.Current.CancellationToken)
			.GetAsyncEnumerator(TestContext.Current.CancellationToken);

		feed.Publish(Sample(7));

		var sample = await NextAsync(watcher);
		Assert.Equal(["gc"], sample.All.Keys);
		Assert.Equal(7, sample["gc"]);
	}

	[Fact]
	public void UnknownCategory_Throws()
	{
		Assert.Throws<KeyNotFoundException>(() =>
			new DiagnosticsFeed().Watch("nothing", TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task StoppedWatcher_NotCounted()
	{
		var feed = new DiagnosticsFeed();
		var watcher = feed.Watch(null, TestContext.Current.CancellationToken)
			.GetAsyncEnumerator(TestContext.Current.CancellationToken);
		Assert.Equal(1, feed.Watchers);
		feed.Publish(Sample(1));
		await NextAsync(watcher);

		await watcher.DisposeAsync();

		Assert.Equal(0, feed.Watchers);
	}
}