using Basil.Application.Abstractions.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Cache;

/// <summary>
///     Read-through <see cref="IMemoryCache" /> decorator over the real <see cref="ISettingsRepository" />,
///     shared by every feature backed by the Settings table (the admin key, the menu icon) rather
///     than one cache per feature.
/// </summary>
public sealed class CachingSettingsRepository(
	ISettingsRepository inner,
	IMemoryCache cache,
	ILogger<CachingSettingsRepository> logger,
	TimeSpan? ttl = null)
	: ISettingsRepository
{
	/// <summary>The entry TTL: the safety net beneath explicit invalidation.</summary>
	private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromMinutes(5);

	/// <inheritdoc />
	public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
	{
		var cacheKey = Key(key);
		if (cache.TryGetValue(cacheKey, out string? cached))
		{
			logger.LogDebug("Cache hit {Key}", cacheKey);
			return cached;
		}

		logger.LogDebug("Cache miss {Key}", cacheKey);
		var value = await inner.GetAsync(key, cancellationToken);
		cache.Set(cacheKey, value, _ttl);
		return value;
	}

	/// <inheritdoc />
	/// <remarks>Invalidates the entry after writing, rather than updating it in place.</remarks>
	public async Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
	{
		await inner.SetAsync(key, value, cancellationToken);
		cache.Remove(Key(key));
	}

	/// <summary>Builds the cache key for a setting.</summary>
	private static string Key(string key)
	{
		return $"Settings:{key}";
	}
}
