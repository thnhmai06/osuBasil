using Basil.Common.Infrastructure.Memory;

namespace Basil.Common.Infrastructure.Tests;

public class IdentityMapTests
{
	[Fact]
	public async Task GetOrAddAsync_FailingLoad_DoesNotPoisonTheKey()
	{
		var map = new IdentityMap<int, string>();
		var secondLoaderRan = false;

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			map.GetOrAddAsync(1, _ => throw new InvalidOperationException(), TestContext.Current.CancellationToken)
				.AsTask());

		var loaded = await map.GetOrAddAsync(1, _ =>
		{
			secondLoaderRan = true;
			return Task.FromResult<string?>("stored item");
		}, TestContext.Current.CancellationToken);

		Assert.True(secondLoaderRan);
		Assert.Equal("stored item", loaded);
	}

	[Fact]
	public async Task GetOrAddAsync_FaultedLoad_DoesNotPoisonTheKey()
	{
		var map = new IdentityMap<int, string>();

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			map.GetOrAddAsync(1, _ => Task.FromException<string?>(new InvalidOperationException()),
				TestContext.Current.CancellationToken).AsTask());

		var loaded = await map.GetOrAddAsync(1, _ => Task.FromResult<string?>("stored item"),
			TestContext.Current.CancellationToken);

		Assert.Equal("stored item", loaded);
	}

	[Fact]
	public async Task GetOrAddAsync_MissingItem_DoesNotKeepTheLoader()
	{
		var map = new IdentityMap<int, string>();

		var missing = await map.GetOrAddAsync(1, _ => Task.FromResult<string?>(null),
			TestContext.Current.CancellationToken);

		Assert.Null(missing);

		var loaded = await map.GetOrAddAsync(1, _ => Task.FromResult<string?>("stored item"),
			TestContext.Current.CancellationToken);

		Assert.Equal("stored item", loaded);
	}

	[Fact]
	public async Task GetOrAddAsync_ConcurrentCallers_RunTheLoaderOnce()
	{
		var map = new IdentityMap<int, string>();
		var runs = 0;
		var gate = new TaskCompletionSource();

		var callers = Enumerable.Range(0, 3).Select(_ => Task.Run(() => map.GetOrAddAsync(1, key =>
		{
			Interlocked.Increment(ref runs);
			return gate.Task.ContinueWith<string?>(_ => "stored item");
		}, TestContext.Current.CancellationToken).AsTask())).ToArray();

		gate.SetResult();
		var loaded = await Task.WhenAll(callers);

		Assert.All(loaded, item => Assert.Equal("stored item", item));
		Assert.Equal(1, runs);
	}
}