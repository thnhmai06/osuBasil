using Basil.Infrastructure.Common.Registry;

namespace Basil.Infrastructure.Common.Tests.Registry;

/// <summary>Contract tests of the cache that sits next to the database.</summary>
public sealed class CacheAsideTests
{
	[Fact]
	public async Task A_value_is_loaded_once_and_then_served_from_the_cache()
	{
		var cache = new CacheAside<int, string>();
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
	public async Task A_missing_value_is_not_cached()
	{
		var cache = new CacheAside<int, string>();
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
	public async Task A_removed_value_is_loaded_again()
	{
		var cache = new CacheAside<int, string>();
		var current = "old";
		await cache.GetAsync(1, () => Task.FromResult<string?>(current));
		current = "new";
		cache.Remove(1);

		Assert.Equal("new", await cache.GetAsync(1, () => Task.FromResult<string?>(current)));
	}

	[Fact]
	public async Task A_value_loaded_while_its_entry_was_removed_is_not_cached()
	{
		var cache = new CacheAside<int, string>();
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