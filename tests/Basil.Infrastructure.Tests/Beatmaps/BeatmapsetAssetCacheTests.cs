using System.IO.Compression;
using Basil.Application.Configurations;
using Basil.Infrastructure.Beatmaps;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Tests.Beatmaps;

/// <summary>
///     Covers <see cref="BeatmapsetAssetCache" /> against a real temp filesystem and a real
///     <c>.osz</c> archive built with <see cref="ZipArchive" />: entry resolution, the cache-hit
///     path skipping the archive entirely, directory-scoped invalidation, and the zip-slip guard on
///     an attacker-influenced entry name.
/// </summary>
public sealed class BeatmapsetAssetCacheTests : IDisposable
{
	private readonly string _cachePath;
	private readonly string _dataDir = Directory.CreateTempSubdirectory("basil-asset-cache-tests-").FullName;
	private readonly BeatmapsetAssetCache _sut;

	public BeatmapsetAssetCacheTests()
	{
		_cachePath = Path.Combine(_dataDir, "Cache");
		var options = Options.Create(new StorageOptions
		{
			ReplaysPath = "", AvatarsPath = "", BeatmapsetsPath = "", MenuSeasonalsPath = "", MenuBannersPath = "",
			FaqsPath = "", CachePath = _cachePath
		});
		_sut = new BeatmapsetAssetCache(options);
	}

	public void Dispose()
	{
		Directory.Delete(_dataDir, true);
	}

	private string MakeOsz(params (string Name, byte[] Content)[] entries)
	{
		var path = Path.Combine(_dataDir, $"{Guid.NewGuid():N}.osz");
		using var stream = File.Create(path);
		using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
		foreach (var (name, content) in entries)
		{
			var entry = archive.CreateEntry(name);
			using var entryStream = entry.Open();
			entryStream.Write(content);
		}

		return path;
	}

	[Fact]
	public async Task ResolveAsync_EntryExists_ExtractsAndReturnsRealFileWithMatchingContent()
	{
		var content = "background bytes"u8.ToArray();
		var osz = MakeOsz(("bg.jpg", content));

		var path = await _sut.ResolveAsync(1, "bg.jpg", osz);

		Assert.NotNull(path);
		Assert.True(File.Exists(path));
		Assert.Equal(content, await File.ReadAllBytesAsync(path));
	}

	[Fact]
	public async Task ResolveAsync_MatchesEntryNameCaseInsensitively()
	{
		var content = "audio bytes"u8.ToArray();
		var osz = MakeOsz(("Audio.MP3", content));

		var path = await _sut.ResolveAsync(2, "audio.mp3", osz);

		Assert.NotNull(path);
		Assert.Equal(content, await File.ReadAllBytesAsync(path));
	}

	[Fact]
	public async Task ResolveAsync_UnknownEntry_ReturnsNull()
	{
		var osz = MakeOsz(("bg.jpg", "x"u8.ToArray()));

		var path = await _sut.ResolveAsync(3, "does-not-exist.jpg", osz);

		Assert.Null(path);
	}

	[Fact]
	public async Task ResolveAsync_SecondCall_ReturnsCachedPathWithoutReopeningTheArchive()
	{
		var content = "first"u8.ToArray();
		var osz = MakeOsz(("bg.jpg", content));

		var first = await _sut.ResolveAsync(4, "bg.jpg", osz);
		File.Delete(osz); // the archive is gone; a real re-extraction attempt would now throw
		var second = await _sut.ResolveAsync(4, "bg.jpg", osz);

		Assert.Equal(first, second);
		Assert.Equal(content, await File.ReadAllBytesAsync(second!));
	}

	[Fact]
	public async Task ResolveAsync_ConcurrentMissesForTheSameEntry_AllResolveTheSameUncorruptedFile()
	{
		var content = new byte[64 * 1024];
		Random.Shared.NextBytes(content);
		var osz = MakeOsz(("audio.mp3", content));

		var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _sut.ResolveAsync(5, "audio.mp3", osz)));

		Assert.All(results, r => Assert.Equal(results[0], r));
		Assert.Equal(content, await File.ReadAllBytesAsync(results[0]!));
		Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(results[0]!)!, "*.tmp"));
	}

	[Fact]
	public async Task Invalidate_RemovesEveryCachedEntryForThatBeatmapsetOnly()
	{
		var osz = MakeOsz(("bg.jpg", "a"u8.ToArray()), ("audio.mp3", "b"u8.ToArray()));
		var bgPath = await _sut.ResolveAsync(6, "bg.jpg", osz);
		var audioPath = await _sut.ResolveAsync(6, "audio.mp3", osz);
		var otherSetPath = await _sut.ResolveAsync(7, "bg.jpg", osz);

		_sut.Invalidate(6);

		Assert.False(File.Exists(bgPath));
		Assert.False(File.Exists(audioPath));
		Assert.True(File.Exists(otherSetPath));
	}

	[Fact]
	public void Invalidate_NothingCached_IsANoOp()
	{
		var exception = Record.Exception(() => _sut.Invalidate(999));

		Assert.Null(exception);
	}

	[Fact]
	public async Task ResolveAsync_EntryNameEscapingTheCacheDirectory_Throws()
	{
		var osz = MakeOsz(("bg.jpg", "x"u8.ToArray()));

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			_sut.ResolveAsync(8, "../../escaped.jpg", osz));
	}
}
