using Basil.Host.Logging;

namespace Basil.Host.Tests.Logging;

/// <summary>Log files nobody wrote to for longer than the retention period are deleted; recent ones and other files stay.</summary>
public sealed class LogRetentionTests : IDisposable
{
	private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
	private readonly string _directory = Directory.CreateTempSubdirectory("basil-logs-").FullName;

	public void Dispose()
	{
		Directory.Delete(_directory, true);
	}

	private string Touch(string name, TimeSpan age)
	{
		var path = Path.Combine(_directory, name);
		File.WriteAllText(path, "x");
		File.SetLastWriteTimeUtc(path, (Now - age).UtcDateTime);
		return path;
	}

	[Fact]
	public void AnOldLogFileIsDeletedAndARecentOneStays()
	{
		var old = Touch("basil-20261001.log", TimeSpan.FromDays(15));
		var recent = Touch("basil-20261028.log", TimeSpan.FromDays(13));

		var deleted = LogRetention.Sweep(_directory, TimeSpan.FromDays(14), Now);

		Assert.Equal(1, deleted);
		Assert.False(File.Exists(old));
		Assert.True(File.Exists(recent));
	}

	[Fact]
	public void OnlyLogFilesAreDeleted()
	{
		var other = Touch("notes.txt", TimeSpan.FromDays(100));

		Assert.Equal(0, LogRetention.Sweep(_directory, TimeSpan.FromDays(14), Now));
		Assert.True(File.Exists(other));
	}

	[Fact]
	public void AMissingFolderIsNotAnError()
	{
		Assert.Equal(0, LogRetention.Sweep(Path.Combine(_directory, "missing"), TimeSpan.FromDays(14), Now));
	}

	[Fact]
	public void TheRetentionPeriodIsFourteenDaysByDefault()
	{
		Assert.Equal(TimeSpan.FromDays(14), new LoggingOptions().RetentionPeriod);
	}
}
