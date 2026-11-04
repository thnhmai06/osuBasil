using Basil.Infrastructure.Common.Storage;
using Basil.Testing.Persistence;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Infrastructure.Common.Tests.Storage;

/// <summary>Contract tests of the upload spool: an upload waits in a file, is cut past its limit and is quarantined when refused.</summary>
public sealed class FileUploadSpoolTests : IDisposable
{
	private static readonly CancellationToken Cancel = TestContext.Current.CancellationToken;
	private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
	private readonly TempData _data = new();

	private FileUploadSpool Spool => new(_data.Paths, _clock);

	public void Dispose()
	{
		_data.Dispose();
	}

	[Fact]
	public async Task A_spooled_upload_reads_back_from_its_start()
	{
		await using var upload = await Spool.SpoolAsync(TestStreams.Of("picture"), 100, Cancel);

		using var reader = new StreamReader(upload.Content);
		Assert.Equal("picture", await reader.ReadToEndAsync(Cancel));
		Assert.False(upload.TooLarge);
	}

	[Fact]
	public async Task An_upload_of_exactly_the_limit_is_not_too_large()
	{
		await using var upload = await Spool.SpoolAsync(TestStreams.Of("12345"), 5, Cancel);

		Assert.False(upload.TooLarge);
	}

	[Fact]
	public async Task An_upload_one_byte_past_the_limit_is_too_large_and_is_not_read_further()
	{
		await using var upload = await Spool.SpoolAsync(TestStreams.Of("123456789"), 5, Cancel);

		Assert.True(upload.TooLarge);
		Assert.Equal(6, upload.Content.Length);
	}

	[Fact]
	public async Task A_disposed_upload_leaves_no_file()
	{
		var upload = await Spool.SpoolAsync(TestStreams.Of("picture"), 100, Cancel);
		await upload.DisposeAsync();

		Assert.Empty(Directory.GetFiles(_data.Paths.Spool));
		Assert.Empty(Directory.GetFiles(_data.Paths.Quarantine));
	}

	[Fact]
	public async Task A_failed_read_leaves_no_file()
	{
		await Assert.ThrowsAsync<IOException>(() => Spool.SpoolAsync(new TestStreams.Failing(), 100, Cancel));

		Assert.Empty(Directory.GetFiles(_data.Paths.Spool));
	}

	[Fact]
	public async Task A_quarantined_upload_is_kept_under_a_name_of_its_own()
	{
		await using var upload = await Spool.SpoolAsync(TestStreams.Of("not a picture"), 100, Cancel);
		await upload.QuarantineAsync(Cancel);

		var kept = Assert.Single(Directory.GetFiles(_data.Paths.Quarantine));
		Assert.Equal("not a picture", await File.ReadAllTextAsync(kept, Cancel));
		Assert.Empty(Directory.GetFiles(_data.Paths.Spool));
	}

	[Fact]
	public async Task A_quarantined_upload_older_than_seven_days_is_deleted_by_the_next_quarantine()
	{
		await using (var old = await Spool.SpoolAsync(TestStreams.Of("old"), 100, Cancel))
		{
			await old.QuarantineAsync(Cancel);
		}

		var oldFile = Assert.Single(Directory.GetFiles(_data.Paths.Quarantine));
		File.SetLastWriteTimeUtc(oldFile, _clock.GetUtcNow().UtcDateTime.AddDays(-8));

		await using var fresh = await Spool.SpoolAsync(TestStreams.Of("fresh"), 100, Cancel);
		await fresh.QuarantineAsync(Cancel);

		var kept = Assert.Single(Directory.GetFiles(_data.Paths.Quarantine));
		Assert.Equal("fresh", await File.ReadAllTextAsync(kept, Cancel));
	}
}
