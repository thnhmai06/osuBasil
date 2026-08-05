using Basil.Infrastructure.Content;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Tests.Content;

/// <summary>
///     xUnit constructs a fresh instance of this class per test method (no <c>IClassFixture</c>), so
///     each test gets its own temp directory and watcher — no shared-state test-order dependency.
/// </summary>
public class FileMotdProviderTests : IDisposable
{
	private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"basil-motd-test-{Guid.NewGuid():N}");
	private FileMotdProvider? _provider;

	private string MotdPath => Path.Combine(_dataDir, "MOTD.txt");

	public void Dispose()
	{
		_provider?.Dispose();
		if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, true);
	}

	[Fact]
	public void GetText_NoFileAtStartup_ReturnsNull()
	{
		_provider = new FileMotdProvider(_dataDir, NullLogger<FileMotdProvider>.Instance);

		Assert.Null(_provider.GetText());
	}

	[Fact]
	public void GetText_FileExistsAtStartup_ReturnsTrimmedContent()
	{
		Directory.CreateDirectory(_dataDir);
		File.WriteAllText(MotdPath, "Welcome to Basil!\n\n");

		_provider = new FileMotdProvider(_dataDir, NullLogger<FileMotdProvider>.Instance);

		Assert.Equal("Welcome to Basil!", _provider.GetText());
	}

	[Fact]
	public async Task GetText_FileCreatedAfterStartup_UpdatesWithinDebounceWindow()
	{
		_provider = new FileMotdProvider(_dataDir, NullLogger<FileMotdProvider>.Instance);
		Assert.Null(_provider.GetText());

		// FileSystemWatcher can silently miss the very first filesystem event after a process's
		// first watcher is armed (a known .NET/Windows cold-start quirk) — a throwaway warm-up
		// event before the real payload avoids that race, matching BeatmapWatcherServiceTests.
		await File.WriteAllTextAsync(Path.Combine(_dataDir, "warmup.txt"), "");
		await Task.Delay(300);
		File.Delete(Path.Combine(_dataDir, "warmup.txt"));

		await File.WriteAllTextAsync(MotdPath, "New message");

		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (DateTime.UtcNow < deadline && _provider.GetText() is null)
			await Task.Delay(100);

		Assert.Equal("New message", _provider.GetText());
	}

	[Fact]
	public async Task GetText_FileDeletedAfterStartup_ReturnsNullWithinDebounceWindow()
	{
		Directory.CreateDirectory(_dataDir);
		await File.WriteAllTextAsync(MotdPath, "Bye");
		_provider = new FileMotdProvider(_dataDir, NullLogger<FileMotdProvider>.Instance);
		Assert.Equal("Bye", _provider.GetText());

		File.Delete(MotdPath);

		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (DateTime.UtcNow < deadline && _provider.GetText() is not null)
			await Task.Delay(100);

		Assert.Null(_provider.GetText());
	}
}