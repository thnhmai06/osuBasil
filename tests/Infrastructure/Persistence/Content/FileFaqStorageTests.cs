using Basil.Application.Persistence.Content;
using Basil.Infrastructure.Persistence.Common;
using Basil.Infrastructure.Persistence.Content;
using Basil.Infrastructure.Persistence.Files;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Persistence.Tests.Content;

/// <summary>Contract tests of the entry writer that FileFaqStorage opens.</summary>
public sealed class FileFaqStorageTests : IDisposable
{
	private const string Entry = "Rules:ScoreSubmission";

	private readonly string _root = Path.Combine(Path.GetTempPath(), "basil-test-faqs-" + Guid.NewGuid().ToString("N"));
	private readonly FileFaqStorage _faqs;

	public FileFaqStorageTests()
	{
		var paths = new DataPaths(Options.Create(new StorageOptions { DataDirectory = _root }));
		_faqs = new FileFaqStorage(paths);
		paths.CreateDirectories();
	}

	[Fact]
	public async Task Commit_stores_the_written_text()
	{
		var writer = await _faqs.CreateOrUpdateAsync(Entry);
		await writer.WriteAsync("New contents");
		writer.Commit();

		Assert.Equal("New contents", await ReadAsync(Entry));
	}

	[Fact]
	public async Task DisposeWithoutCommit_keeps_the_previous_entry_text()
	{
		await StoreAsync(Entry, "Version 1");

		var writer = await _faqs.CreateOrUpdateAsync(Entry);
		await writer.WriteAsync("Partial write: only the half of a");
		await writer.DisposeAsync();

		Assert.Equal("Version 1", await ReadAsync(Entry));
	}

	[Fact]
	public async Task DisposeAfterCommit_keeps_the_committed_text()
	{
		var writer = await _faqs.CreateOrUpdateAsync(Entry);
		await writer.WriteAsync("Committed text");
		writer.Commit();
		await writer.DisposeAsync();

		Assert.Equal("Committed text", await ReadAsync(Entry));
	}

	private async ValueTask StoreAsync(string entry, string text)
	{
		var writer = await _faqs.CreateOrUpdateAsync(entry);
		await writer.WriteAsync(text);
		writer.Commit();
	}

	/// <summary>Reads the full text of the entry; an empty string when no entry has the name.</summary>
	private async ValueTask<string> ReadAsync(string entry)
	{
		var reader = await _faqs.ReadAsync(entry);
		if (reader is null) return string.Empty;

		using (reader)
		{
			return await reader.ReadToEndAsync();
		}
	}

	public void Dispose()
	{
		Directory.Delete(_root, recursive: true);
	}
}
