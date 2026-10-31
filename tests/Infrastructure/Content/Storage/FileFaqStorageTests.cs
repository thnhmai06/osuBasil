using Basil.Application.Content.Storage;
using Basil.Infrastructure.Content.Storage;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Content.Tests.Storage;

/// <summary>Contract tests of FileFaqStorage, read through IFaqStorage and the entry writer it opens.</summary>
public sealed class FileFaqStorageTests : IDisposable
{
	private const string Entry = "Rules:ScoreSubmission";

	private readonly TempData _data = new();
	private readonly IFaqStorage _faqs;

	public FileFaqStorageTests()
	{
		_faqs = new FileFaqStorage(_data.Paths);
	}

	public void Dispose()
	{
		_data.Dispose();
	}

	[Fact]
	public async Task Commit_stores_the_written_text()
	{
		var writer = await _faqs.CreateOrUpdateAsync(Entry, TestContext.Current.CancellationToken);
		await writer.WriteAsync("New contents");
		writer.Commit();

		Assert.Equal("New contents", await ReadAsync(Entry));
	}

	[Fact]
	public async Task DisposeWithoutCommit_keeps_the_previous_entry_text()
	{
		await StoreAsync(Entry, "Version 1");

		var writer = await _faqs.CreateOrUpdateAsync(Entry, TestContext.Current.CancellationToken);
		await writer.WriteAsync("Partial write: only the half of a");
		await writer.DisposeAsync();

		Assert.Equal("Version 1", await ReadAsync(Entry));
	}

	[Fact]
	public async Task DisposeAfterCommit_keeps_the_committed_text()
	{
		var writer = await _faqs.CreateOrUpdateAsync(Entry, TestContext.Current.CancellationToken);
		await writer.WriteAsync("Committed text");
		writer.Commit();
		await writer.DisposeAsync();

		Assert.Equal("Committed text", await ReadAsync(Entry));
	}

	[Fact]
	public async Task The_entry_keeps_its_previous_text_until_the_writer_commits()
	{
		await StoreAsync(Entry, "Version 1");

		await using var writer = await _faqs.CreateOrUpdateAsync(Entry, TestContext.Current.CancellationToken);
		await writer.WriteAsync("Version 2");

		Assert.Equal("Version 1", await ReadAsync(Entry));
	}

	[Fact]
	public async Task
		A_new_entry_is_neither_listed_nor_readable_until_the_writer_commits_and_not_at_all_if_it_never_does()
	{
		var writer = await _faqs.CreateOrUpdateAsync(Entry, TestContext.Current.CancellationToken);
		await writer.WriteAsync("Draft");

		Assert.Empty(await _faqs.ListAsync(TestContext.Current.CancellationToken));
		Assert.Null(await _faqs.ReadAsync(Entry, TestContext.Current.CancellationToken));

		await writer.DisposeAsync();

		Assert.Empty(await _faqs.ListAsync(TestContext.Current.CancellationToken));
		Assert.Null(await _faqs.ReadAsync(Entry, TestContext.Current.CancellationToken));
		Assert.Empty(TempData.FilesUnder(_data.Paths.Faqs.Path));
	}

	[Fact]
	public async Task Committing_again_changes_nothing()
	{
		var writer = await _faqs.CreateOrUpdateAsync(Entry, TestContext.Current.CancellationToken);
		await writer.WriteAsync("Committed text");
		writer.Commit();

		writer.Commit();

		Assert.Equal("Committed text", await ReadAsync(Entry));
	}

	[Fact]
	public async Task A_discarded_update_of_one_entry_leaves_the_others_as_they_were_committed()
	{
		await StoreAsync("Rules:A", "A1");
		await StoreAsync("Rules:B", "B1");

		var writerA = await _faqs.CreateOrUpdateAsync("Rules:A", TestContext.Current.CancellationToken);
		await writerA.WriteAsync("A2");
		writerA.Commit();
		var writerB = await _faqs.CreateOrUpdateAsync("Rules:B", TestContext.Current.CancellationToken);
		await writerB.WriteAsync("B2");
		await writerB.DisposeAsync();

		Assert.Equal("A2", await ReadAsync("Rules:A"));
		Assert.Equal("B1", await ReadAsync("Rules:B"));
	}

	[Fact]
	public async Task The_listed_names_are_those_of_the_stored_entries_with_colons_for_folders()
	{
		await StoreAsync("General", "1");
		await StoreAsync("Rules:ScoreSubmission", "2");
		await StoreAsync("Rules:Conduct:Chat", "3");

		var listed = await _faqs.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(["General", "Rules:Conduct:Chat", "Rules:ScoreSubmission"], listed.Order(StringComparer.Ordinal));
	}

	[Fact]
	public async Task Nothing_is_listed_when_no_entry_is_stored()
	{
		Assert.Empty(await _faqs.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_nested_entry_reads_back_under_its_nested_name()
	{
		await StoreAsync("Rules:Conduct:Chat", "Be kind");

		Assert.Equal("Be kind", await ReadAsync("Rules:Conduct:Chat"));
	}

	[Fact]
	public async Task Reading_an_entry_that_is_not_stored_gives_null()
	{
		Assert.Null(await _faqs.ReadAsync("Missing:Entry", TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_deleted_entry_is_no_longer_stored_and_the_others_are()
	{
		await StoreAsync("Gone", "1");
		await StoreAsync("Kept", "2");

		await _faqs.DeleteAsync("Gone", TestContext.Current.CancellationToken);

		Assert.Null(await _faqs.ReadAsync("Gone", TestContext.Current.CancellationToken));
		Assert.Equal(["Kept"], await _faqs.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Deleting_an_entry_that_is_not_stored_does_nothing()
	{
		await _faqs.DeleteAsync("Missing:Entry", TestContext.Current.CancellationToken);

		Assert.Empty(await _faqs.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task An_entry_name_that_climbs_out_of_the_directory_writes_nothing_outside_it()
	{
		var outside = Path.Combine(_data.Root, "escape.txt");

		var failure = await Record.ExceptionAsync(async () =>
		{
			var writer = await _faqs.CreateOrUpdateAsync("../escape", TestContext.Current.CancellationToken);
			await writer.WriteAsync("Escaped");
			writer.Commit();
		});

		Assert.False(File.Exists(outside), failure?.Message);
	}

	[Fact]
	public async Task An_entry_name_that_climbs_out_of_the_directory_reads_nothing_outside_it()
	{
		await File.WriteAllTextAsync(Path.Combine(_data.Root, "secret.txt"), "Secret",
			TestContext.Current.CancellationToken);
		string? read = null;

		var failure = await Record.ExceptionAsync(async () =>
		{
			var reader = await _faqs.ReadAsync("../secret", TestContext.Current.CancellationToken);
			read = reader is null ? null : await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
		});

		Assert.Null(read);
		Assert.True(failure is null or ArgumentException, failure?.ToString());
	}

	[Fact]
	public async Task An_entry_name_that_climbs_out_of_the_directory_deletes_nothing_outside_it()
	{
		var outside = Path.Combine(_data.Root, "secret.txt");
		await File.WriteAllTextAsync(outside, "Secret", TestContext.Current.CancellationToken);

		await Record.ExceptionAsync(() => _faqs.DeleteAsync("../secret", TestContext.Current.CancellationToken));

		Assert.True(File.Exists(outside));
	}

	[Theory]
	[InlineData("")]
	[InlineData("  ")]
	[InlineData("CON")]
	[InlineData("NUL")]
	[InlineData("COM1")]
	[InlineData("LPT1")]
	[InlineData("Rules:CON")]
	public async Task An_entry_name_that_names_no_file_or_a_device_is_refused_and_stores_nothing(string entry)
	{
		await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
		{
			var writer = await _faqs.CreateOrUpdateAsync(entry, TestContext.Current.CancellationToken);
			await writer.WriteAsync("Text");
			writer.Commit();
		});

		Assert.Empty(await _faqs.ListAsync(TestContext.Current.CancellationToken));
	}

	private async ValueTask StoreAsync(string entry, string text)
	{
		var writer = await _faqs.CreateOrUpdateAsync(entry, TestContext.Current.CancellationToken);
		await writer.WriteAsync(text);
		writer.Commit();
	}

	/// <summary>Reads the full text of the entry; an empty string when no entry has the name.</summary>
	private async ValueTask<string> ReadAsync(string entry)
	{
		var reader = await _faqs.ReadAsync(entry, TestContext.Current.CancellationToken);
		if (reader is null) return string.Empty;

		using (reader)
		{
			return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
		}
	}
}