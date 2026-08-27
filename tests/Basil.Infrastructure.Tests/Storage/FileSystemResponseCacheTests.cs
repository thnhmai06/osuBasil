using Basil.Application.Configurations;
using Basil.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Tests.Storage;

public class FileSystemResponseCacheTests : IDisposable
{
	private readonly string _cachePath = Directory.CreateTempSubdirectory("basil-response-cache-tests-").FullName;
	private readonly FileSystemResponseCache _cache;

	public FileSystemResponseCacheTests()
	{
		_cache = new FileSystemResponseCache(Options.Create(new StorageOptions
		{
			ReplaysPath = "", AvatarsPath = "", MapsetsPath = "", MenuSeasonalsPath = "", MenuBannersPath = "",
			FaqsPath = "", CachePath = _cachePath
		}));
	}

	public void Dispose()
	{
		Directory.Delete(_cachePath, true);
	}

	[Fact]
	public async Task GetAsync_UncachedKey_ReturnsNull()
	{
		var result = await _cache.GetAsync("thumbs", "1.jpg");

		Assert.Null(result);
	}

	[Fact]
	public async Task PutThenGet_RoundTripsContent()
	{
		var content = new byte[] { 1, 2, 3 };

		await _cache.PutAsync("thumbs", "1.jpg", content);
		var result = await _cache.GetAsync("thumbs", "1.jpg");

		Assert.Equal(content, result);
	}

	/// <summary>
	///     Regression test: PutAsync used to write directly to the final path, so a concurrent
	///     GetAsync during a regeneration could observe a partially-written file. It now writes to
	///     a sibling temp file and renames it into place; this pins that no temp file is left
	///     behind and that overwriting an existing key still replaces its content correctly.
	/// </summary>
	[Fact]
	public async Task PutAsync_LeavesNoTempFileBehind_AndOverwriteReplacesContent()
	{
		await _cache.PutAsync("thumbs", "1.jpg", [1, 2, 3]);
		await _cache.PutAsync("thumbs", "1.jpg", [4, 5, 6, 7]);

		var result = await _cache.GetAsync("thumbs", "1.jpg");
		Assert.Equal(new byte[] { 4, 5, 6, 7 }, result);

		var entryDir = Path.Combine(_cachePath, "thumbs");
		Assert.DoesNotContain(Directory.GetFiles(entryDir), f => f.EndsWith(".tmp", StringComparison.Ordinal));
	}

	[Fact]
	public async Task DeleteAsync_RemovesCachedEntry()
	{
		await _cache.PutAsync("thumbs", "1.jpg", [1, 2, 3]);

		await _cache.DeleteAsync("thumbs", "1.jpg");

		Assert.Null(await _cache.GetAsync("thumbs", "1.jpg"));
	}

	[Fact]
	public async Task DeleteAsync_UncachedKey_IsNoOp()
	{
		await _cache.DeleteAsync("thumbs", "missing.jpg");
	}
}