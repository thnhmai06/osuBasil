using Basil.Infrastructure.Beatmaps.Storage;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Beatmaps.Tests.Storage;

/// <summary>
///     What the beatmapset storage cleans up at startup: what it was writing or setting aside when the server
///     stopped.
/// </summary>
public sealed class IncomingLeftoversTests : IDisposable
{
	private readonly TempData _data = new();

	private string Sets => _data.Paths.Beatmapsets.Path;

	public void Dispose()
	{
		_data.Dispose();
	}

	[Fact]
	public async Task LoadAsync_RemovesIncomingFoldersAndFiles()
	{
		Directory.CreateDirectory(Path.Combine(Sets, ".incoming_folder"));
		File.WriteAllText(Path.Combine(Sets, ".incoming_folder", "archive.osz"), "inside");
		File.WriteAllText(Path.Combine(Sets, ".incoming_upload.osz"), "x");

		await new IncomingLeftovers(_data.Paths).LoadAsync(TestContext.Current.CancellationToken);

		Assert.Empty(Directory.EnumerateFileSystemEntries(Sets));
	}

	[Fact]
	public async Task LoadAsync_KeepsStoredSets()
	{
		Directory.CreateDirectory(Path.Combine(Sets, "1 Artist - Title"));
		File.WriteAllText(Path.Combine(Sets, "2.osz"), "x");

		await new IncomingLeftovers(_data.Paths).LoadAsync(TestContext.Current.CancellationToken);

		Assert.True(Directory.Exists(Path.Combine(Sets, "1 Artist - Title")));
		Assert.True(File.Exists(Path.Combine(Sets, "2.osz")));
	}
}