using System.Text.Json;
using Basil.Bot.Replies;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Bot.Tests.Replies;

/// <summary>Contract tests of the reading of the replies file while the server runs.</summary>
public sealed class RepliesWatcherTests : IDisposable
{
	private static readonly Reply Plain = new("test.plain", "You're not in a match.");

	private readonly string _directory =
		Path.Combine(Path.GetTempPath(), "basil-watcher-" + Guid.NewGuid().ToString("N"));

	private readonly FakeTimeProvider _time = new();

	private string FilePath => Path.Combine(_directory, "replies.json");

	public void Dispose()
	{
		if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
	}

	[Fact]
	public async Task Starting_reads_the_replies_file()
	{
		WriteFile("Not in a match.");
		var (catalog, watcher) = New();

		await watcher.StartAsync(TestContext.Current.CancellationToken);

		Assert.Equal("Not in a match.", catalog.Text(Plain));
		await watcher.StopAsync(TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task Starting_without_a_replies_file_writes_it_with_the_default_wording()
	{
		var (_, watcher) = New();

		await watcher.StartAsync(TestContext.Current.CancellationToken);

		Assert.True(File.Exists(FilePath));
		await watcher.StopAsync(TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task A_change_of_the_file_takes_effect_once_it_has_settled()
	{
		WriteFile("Not in a match.");
		var (catalog, watcher) = New();
		await watcher.StartAsync(TestContext.Current.CancellationToken);
		WriteFile("No match for you.");

		watcher.Changed();
		_time.Advance(TimeSpan.FromSeconds(2));

		Assert.Equal("No match for you.", catalog.Text(Plain));
		await watcher.StopAsync(TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task A_change_does_not_take_effect_before_it_has_settled()
	{
		WriteFile("Not in a match.");
		var (catalog, watcher) = New();
		await watcher.StartAsync(TestContext.Current.CancellationToken);
		WriteFile("No match for you.");

		watcher.Changed();
		_time.Advance(TimeSpan.FromMilliseconds(100));

		Assert.Equal("Not in a match.", catalog.Text(Plain));
		await watcher.StopAsync(TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task Several_changes_in_a_row_are_read_once_after_the_last()
	{
		WriteFile("Not in a match.");
		var (catalog, watcher) = New();
		await watcher.StartAsync(TestContext.Current.CancellationToken);
		WriteFile("First draft.");
		watcher.Changed();
		_time.Advance(TimeSpan.FromMilliseconds(300));
		WriteFile("Final wording.");
		watcher.Changed();

		_time.Advance(TimeSpan.FromMilliseconds(300));
		Assert.Equal("Not in a match.", catalog.Text(Plain));
		_time.Advance(TimeSpan.FromMilliseconds(300));

		Assert.Equal("Final wording.", catalog.Text(Plain));
		await watcher.StopAsync(TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task A_change_to_a_file_that_cannot_be_parsed_leaves_the_wording_in_use()
	{
		WriteFile("Not in a match.");
		var (catalog, watcher) = New();
		await watcher.StartAsync(TestContext.Current.CancellationToken);
		File.WriteAllText(FilePath, "{ not json");

		watcher.Changed();
		_time.Advance(TimeSpan.FromSeconds(2));

		Assert.Equal("Not in a match.", catalog.Text(Plain));
		await watcher.StopAsync(TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task A_change_after_stopping_is_not_read()
	{
		WriteFile("Not in a match.");
		var (catalog, watcher) = New();
		await watcher.StartAsync(TestContext.Current.CancellationToken);
		await watcher.StopAsync(TestContext.Current.CancellationToken);
		WriteFile("No match for you.");

		watcher.Changed();
		_time.Advance(TimeSpan.FromSeconds(2));

		Assert.Equal("Not in a match.", catalog.Text(Plain));
	}

	private (ReplyCatalog Catalog, RepliesWatcher Watcher) New()
	{
		var catalog = new ReplyCatalog([Plain], Options.Create(new RepliesOptions { Path = FilePath }),
			NullLogger<ReplyCatalog>.Instance);
		return (catalog,
			new RepliesWatcher(catalog, _time, NullLogger<RepliesWatcher>.Instance) { WatchFileSystem = false });
	}

	private void WriteFile(string plain)
	{
		Directory.CreateDirectory(_directory);
		File.WriteAllText(FilePath, JsonSerializer.Serialize(new Dictionary<string, string> { [Plain.Key] = plain }));
	}
}