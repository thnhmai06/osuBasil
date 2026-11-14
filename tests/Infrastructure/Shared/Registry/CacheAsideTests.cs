using Basil.Infrastructure.Shared.Registry;
using Microsoft.Extensions.Caching.Memory;

namespace Basil.Infrastructure.Shared.Tests.Registry;

/// <summary>Contract tests of the cache that sits next to the database.</summary>
public sealed class CacheAsideTests
{
	[Fact]
	public async Task ValueLoadedOnce_ServedFromCache()
	{
		var cache = new CacheAside<int, string>(new MemoryCache(new MemoryCacheOptions()));
		var loads = 0;

		Task<string?> Load()
		{
			loads++;
			return Task.FromResult<string?>("hash");
		}

		await cache.GetAsync(1, Load);
		var second = await cache.GetAsync(1, Load);

		Assert.Equal("hash", second);
		Assert.Equal(1, loads);
	}

	[Fact]
	public async Task MissingValue_NotCached()
	{
		var cache = new CacheAside<int, string>(new MemoryCache(new MemoryCacheOptions()));
		var loads = 0;

		Task<string?> Load()
		{
			loads++;
			return Task.FromResult<string?>(null);
		}

		Assert.Null(await cache.GetAsync(1, Load));
		Assert.Null(await cache.GetAsync(1, Load));

		Assert.Equal(2, loads);
	}

	[Fact]
	public async Task RemovedValue_LoadedAgain()
	{
		var cache = new CacheAside<int, string>(new MemoryCache(new MemoryCacheOptions()));
		var current = "old";
		await cache.GetAsync(1, () => Task.FromResult<string?>(current));
		current = "new";
		cache.Remove(1);

		Assert.Equal("new", await cache.GetAsync(1, () => Task.FromResult<string?>(current)));
	}

	[Fact]
	public async Task EntryRemovedConcurrently_NotCached()
	{
		var cache = new CacheAside<int, string>(new MemoryCache(new MemoryCacheOptions()));
		var current = "old";
		var loading = new TaskCompletionSource();
		var release = new TaskCompletionSource();

		var read = cache.GetAsync(1, async () =>
		{
			var seen = current;
			loading.SetResult();
			await release.Task;
			return seen;
		});
		await loading.Task;
		current = "new";
		cache.Remove(1);
		release.SetResult();
		Assert.Equal("old", await read);

		Assert.Equal("new", await cache.GetAsync(1, () => Task.FromResult<string?>(current)));
	}
}