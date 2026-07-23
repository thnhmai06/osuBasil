using Basil.Application.Configuration;
using Basil.Infrastructure.Beatmaps;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Tests.Beatmaps;

/// <summary>
///     The GC pass runs once immediately on start (before its own 10-minute timer), so this can be
///     tested without waiting out the real interval.
/// </summary>
public class MapsetGarbageCollectorServiceTests : IDisposable
{
    private readonly string _mapsetsPath;
    private readonly MapsetGarbageCollectorService _service;

    public MapsetGarbageCollectorServiceTests()
    {
        _mapsetsPath = Path.Combine(Path.GetTempPath(), "obt-gc-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_mapsetsPath);
        var options = Options.Create(new StorageOptions
        {
            ReplaysPath = "", AvatarsPath = "", MapsetsPath = _mapsetsPath, SeasonalsPath = "", FaqsPath = ""
        });
        _service = new MapsetGarbageCollectorService(options, NullLogger<MapsetGarbageCollectorService>.Instance);
    }

    public void Dispose()
    {
        Directory.Delete(_mapsetsPath, true);
    }

    [Fact]
    public async Task StartAsync_DeletesMarkedFoldersImmediately_LeavesLiveFoldersAlone()
    {
        var deletedFolder = Path.Combine(_mapsetsPath, "5 Artist - Title" + BeatmapIngestionService.DeletedFolderInfix + "abc");
        var liveFolder = Path.Combine(_mapsetsPath, "6 Artist - Title");
        Directory.CreateDirectory(deletedFolder);
        Directory.CreateDirectory(liveFolder);

        await _service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && Directory.Exists(deletedFolder))
                await Task.Delay(100);

            Assert.False(Directory.Exists(deletedFolder));
            Assert.True(Directory.Exists(liveFolder));
        }
        finally
        {
            await _service.StopAsync(CancellationToken.None);
        }
    }
}
