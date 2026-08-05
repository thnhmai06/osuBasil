using Basil.Application.Abstractions.Settings;
using Basil.Infrastructure.Cache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Tests.Caching;

public class CachingSettingsRepositoryTests
{
	[Fact]
	public async Task GetAsync_SecondCall_DoesNotHitInner()
	{
		var inner = new CountingSettingsRepository { Values = { ["AdminKey:Hash"] = "hash" } };
		var repo = new CachingSettingsRepository(inner, new MemoryCache(new MemoryCacheOptions()),
			NullLogger<CachingSettingsRepository>.Instance);

		await repo.GetAsync("AdminKey:Hash");
		await repo.GetAsync("AdminKey:Hash");

		Assert.Equal(1, inner.GetCalls);
	}

	[Fact]
	public async Task GetAsync_NullValue_IsStillCached()
	{
		var inner = new CountingSettingsRepository();
		var repo = new CachingSettingsRepository(inner, new MemoryCache(new MemoryCacheOptions()),
			NullLogger<CachingSettingsRepository>.Instance);

		await repo.GetAsync("AdminKey:Hash");
		await repo.GetAsync("AdminKey:Hash");

		Assert.Equal(1, inner.GetCalls);
	}

	[Fact]
	public async Task SetAsync_InvalidatesCachedEntry()
	{
		var inner = new CountingSettingsRepository { Values = { ["AdminKey:Hash"] = "hash" } };
		var repo = new CachingSettingsRepository(inner, new MemoryCache(new MemoryCacheOptions()),
			NullLogger<CachingSettingsRepository>.Instance);

		await repo.GetAsync("AdminKey:Hash");
		await repo.SetAsync("AdminKey:Hash", "new-hash");
		var value = await repo.GetAsync("AdminKey:Hash");

		Assert.Equal(2, inner.GetCalls);
		Assert.Equal("new-hash", value);
	}

	[Fact]
	public async Task DifferentKeys_BothHitInner()
	{
		var inner = new CountingSettingsRepository
		{
			Values = { ["AdminKey:Hash"] = "a", ["MenuIcon:Path"] = "b" }
		};
		var repo = new CachingSettingsRepository(inner, new MemoryCache(new MemoryCacheOptions()),
			NullLogger<CachingSettingsRepository>.Instance);

		await repo.GetAsync("AdminKey:Hash");
		await repo.GetAsync("MenuIcon:Path");

		Assert.Equal(2, inner.GetCalls);
	}

	private sealed class CountingSettingsRepository : ISettingsRepository
	{
		public int GetCalls { get; private set; }
		public Dictionary<string, string?> Values { get; } = new();

		public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
		{
			GetCalls++;
			return Task.FromResult(Values.GetValueOrDefault(key));
		}

		public Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
		{
			Values[key] = value;
			return Task.CompletedTask;
		}
	}
}