using Basil.Application.Beatmaps.Persistence;
using Basil.Infrastructure.Beatmaps.Storage;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Beatmaps.Tests.Storage;

/// <summary>Contract tests of the reading of an entry of the beatmapset storage.</summary>
public sealed class FileBeatmapsetEntryReaderTests : IDisposable
{
	private readonly TempData _data = new();

	private string Sets => _data.Paths.Beatmapsets.Path;

	public void Dispose()
	{
		_data.Dispose();
	}

	[Fact]
	public void VanishingFolder_ReadsAsMissing()
	{
		Directory.CreateDirectory(Path.Combine(Sets, "7 Artist - Title"));

		var entry = FileBeatmapsetEntryReader.EntryOf(Sets, "7 Artist - Title", Vanished);

		var folder = Assert.IsAssignableFrom<BeatmapsetFolder>(entry);
		Assert.False(folder.Exists);
		Assert.Equal(7, folder.Id);
	}

	[Fact]
	public void VanishingArchive_NoEntry()
	{
		File.WriteAllBytes(Path.Combine(Sets, "7 Artist - Title.osz"), [1]);

		Assert.Null(FileBeatmapsetEntryReader.EntryOf(Sets, "7 Artist - Title.osz", Vanished));
	}

	private static FileAttributes Vanished(string path)
	{
		throw new FileNotFoundException(null, path);
	}
}