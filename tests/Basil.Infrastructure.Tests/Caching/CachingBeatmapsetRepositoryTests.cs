using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Cache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Tests.Caching;

public class CachingBeatmapsetRepositoryTests
{
	private static Beatmapset MakeMapset(int id)
	{
		return new Beatmapset(id, "Artist", "Title", "Creator", DateTime.UnixEpoch, DateTime.UnixEpoch);
	}

	[Fact]
	public async Task FetchByIdAsync_SecondCall_DoesNotHitInner()
	{
		var inner = new CountingBeatmapsetRepository
		{
			ById =
			{
				[1] = MakeMapset(1)
			}
		};
		var repo = new CachingBeatmapsetRepository(inner, new MemoryCache(new MemoryCacheOptions()),
			NullLogger<CachingBeatmapsetRepository>.Instance);

		await repo.FetchByIdAsync(1);
		await repo.FetchByIdAsync(1);

		Assert.Equal(1, inner.FetchByIdCalls);
	}

	[Fact]
	public async Task SetFrozenAsync_InvalidatesCachedEntry()
	{
		var inner = new CountingBeatmapsetRepository
		{
			ById =
			{
				[1] = MakeMapset(1)
			}
		};
		var repo = new CachingBeatmapsetRepository(inner, new MemoryCache(new MemoryCacheOptions()),
			NullLogger<CachingBeatmapsetRepository>.Instance);

		await repo.FetchByIdAsync(1);
		await repo.SetFrozenAsync(1, true);
		await repo.FetchByIdAsync(1);

		Assert.Equal(2, inner.FetchByIdCalls);
	}

	[Fact]
	public async Task SetPrivateAsync_InvalidatesCachedEntry()
	{
		var inner = new CountingBeatmapsetRepository
		{
			ById =
			{
				[1] = MakeMapset(1)
			}
		};
		var repo = new CachingBeatmapsetRepository(inner, new MemoryCache(new MemoryCacheOptions()),
			NullLogger<CachingBeatmapsetRepository>.Instance);

		await repo.FetchByIdAsync(1);
		await repo.SetPrivateAsync(1, true);
		await repo.FetchByIdAsync(1);

		Assert.Equal(2, inner.FetchByIdCalls);
	}

	[Fact]
	public async Task SetBackgroundFileAsync_InvalidatesCachedEntry()
	{
		var inner = new CountingBeatmapsetRepository
		{
			ById =
			{
				[1] = MakeMapset(1)
			}
		};
		var repo = new CachingBeatmapsetRepository(inner, new MemoryCache(new MemoryCacheOptions()),
			NullLogger<CachingBeatmapsetRepository>.Instance);

		await repo.FetchByIdAsync(1);
		await repo.SetBackgroundFileAsync(1, "bg.jpg");
		await repo.FetchByIdAsync(1);

		Assert.Equal(2, inner.FetchByIdCalls);
	}

	[Fact]
	public async Task UpsertAsync_InvalidatesCachedEntry()
	{
		var inner = new CountingBeatmapsetRepository();
		var original = MakeMapset(1);
		inner.ById[1] = original;
		var repo = new CachingBeatmapsetRepository(inner, new MemoryCache(new MemoryCacheOptions()),
			NullLogger<CachingBeatmapsetRepository>.Instance);

		await repo.FetchByIdAsync(1);
		var updated = original with { Artist = "New Artist" };
		inner.UpsertResult = updated;
		await repo.UpsertAsync(updated);
		inner.ById[1] = updated;
		await repo.FetchByIdAsync(1);

		Assert.Equal(2, inner.FetchByIdCalls);
	}

	private sealed class CountingBeatmapsetRepository : IBeatmapsetRepository
	{
		public int FetchByIdCalls { get; private set; }
		public Dictionary<int, Beatmapset> ById { get; } = new();
		public Beatmapset? UpsertResult { get; set; }

		public Task<Beatmapset?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
		{
			FetchByIdCalls++;
			return Task.FromResult(ById.GetValueOrDefault(id));
		}

		public Task<Beatmapset> UpsertAsync(Beatmapset beatmapset, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(UpsertResult ?? beatmapset);
		}

		public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult(0);
		}

		public Task<IReadOnlyList<int>> FetchAllIdsAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<int>>([]);
		}

		public Task<IReadOnlyList<Beatmapset>> FetchPageAsync(int offset, int limit, bool onlyWithVisibleBeatmaps,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<Beatmapset>>([]);
		}

		public Task SetFrozenAsync(int id, bool frozen, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task SetPrivateAsync(int id, bool isPrivate, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task SetBackgroundFileAsync(int id, string? backgroundFile,
			CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task SetAudioFileAsync(int id, string? audioFile, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task<int> FetchCountAsync(bool includePrivate, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(0);
		}
	}
}