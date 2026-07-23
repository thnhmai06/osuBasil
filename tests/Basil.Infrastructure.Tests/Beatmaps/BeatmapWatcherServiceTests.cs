using Basil.Application.Configuration;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.Persistence.Repositories;
using Basil.Infrastructure.Tests.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Tests.Beatmaps;

/// <summary>
///     Thin glue over BeatmapIngestionService's already-tested reconciliation methods — one
///     integration-style test dropping a real mapset folder in and polling for the DB row is enough.
/// </summary>
public class BeatmapWatcherServiceTests : IClassFixture<SqliteFixture>, IDisposable
{
    private readonly SqliteMapRepository _maps;
    private readonly string _mapsetsPath;
    private readonly BeatmapWatcherService _watcher;
    private readonly CapturingLogger<BeatmapIngestionService> _ingestionLog = new();
    private readonly CapturingLogger<BeatmapWatcherService> _watcherLog = new();

    public BeatmapWatcherServiceTests(SqliteFixture fixture)
    {
        _maps = new SqliteMapRepository(fixture.ConnectionString);
        var mapsets = new SqliteMapsetRepository(fixture.ConnectionString);
        _mapsetsPath = Path.Combine(Path.GetTempPath(), "obt-watcher-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_mapsetsPath);

        var options = Options.Create(new StorageOptions
        {
            ReplaysPath = "", AvatarsPath = "", MapsetsPath = _mapsetsPath, SeasonalsPath = "", FaqsPath = ""
        });
        var scores = new SqliteScoreRepository(fixture.ConnectionString);
        var ingestion = new BeatmapIngestionService(_maps, mapsets, scores, new FakeDifficultyCalculator(), options, _ingestionLog);
        _watcher = new BeatmapWatcherService(ingestion, options, _watcherLog);
    }

    public void Dispose()
    {
        Directory.Delete(_mapsetsPath, true);
    }

    [Fact]
    public async Task DroppingMapsetFolder_GetsAutoIngestedWithinDebounceWindow()
    {
        await _watcher.StartAsync(CancellationToken.None);
        try
        {
            // FileSystemWatcher can silently miss the very first filesystem event after a process's
            // first watcher is armed (a known .NET/Windows cold-start quirk) — a throwaway warm-up
            // event before the real payload avoids that race.
            await File.WriteAllTextAsync(Path.Combine(_mapsetsPath, "warmup.txt"), "");
            await Task.Delay(300);
            File.Delete(Path.Combine(_mapsetsPath, "warmup.txt"));

            var folder = Path.Combine(_mapsetsPath, "900000000 FAIRY FORE - Vivid");
            Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "vivid_osu_file.osu"),
                Path.Combine(folder, "vivid_osu_file.osu"));

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline &&
                   await _maps.FetchOneAsync(filename: "vivid_osu_file.osu", includePrivate: true) is null)
                await Task.Delay(200);

            var found = await _maps.FetchOneAsync(filename: "vivid_osu_file.osu", includePrivate: true);
            Assert.True(found is not null,
                "Beatmap never appeared. Ingestion log: " + string.Join(" | ", _ingestionLog.Messages) +
                " || Watcher log: " + string.Join(" | ", _watcherLog.Messages));
        }
        finally
        {
            await _watcher.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RenamingMapsetFolderToDeletedMarker_RemovesMapsetWithoutReingestingIt()
    {
        await _watcher.StartAsync(CancellationToken.None);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(_mapsetsPath, "warmup.txt"), "");
            await Task.Delay(300);
            File.Delete(Path.Combine(_mapsetsPath, "warmup.txt"));

            var folder = Path.Combine(_mapsetsPath, "unresolved FAIRY FORE - Vivid");
            Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "vivid_osu_file.osu"),
                Path.Combine(folder, "vivid_osu_file.osu"));

            var ingestDeadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < ingestDeadline &&
                   await _maps.FetchOneAsync(filename: "vivid_osu_file.osu", includePrivate: true) is null)
                await Task.Delay(200);

            var beatmap = await _maps.FetchOneAsync(filename: "vivid_osu_file.osu", includePrivate: true);
            Assert.NotNull(beatmap);

            // ReconcileDeletedFolderAsync (which this test exercises indirectly through the watcher)
            // parses the Mapset id from the folder's own leading digits — rename to the actually
            // resolved id first, matching every other test here that relies on that lookup.
            var resolvedFolder = BeatmapIngestionService.MapsetFolderPath(
                new StorageOptions { ReplaysPath = "", AvatarsPath = "", MapsetsPath = _mapsetsPath, SeasonalsPath = "", FaqsPath = "" },
                beatmap!.Mapset);
            Directory.Move(folder, resolvedFolder);

            var deletedFolder = resolvedFolder + BeatmapIngestionService.DeletedFolderInfix + Guid.NewGuid().ToString("N");
            Directory.Move(resolvedFolder, deletedFolder);

            var deleteDeadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deleteDeadline &&
                   await _maps.FetchOneAsync(setId: beatmap!.Mapset.Id, includePrivate: true) is not null)
                await Task.Delay(200);

            Assert.Null(await _maps.FetchOneAsync(setId: beatmap!.Mapset.Id, includePrivate: true));
        }
        finally
        {
            await _watcher.StopAsync(CancellationToken.None);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public readonly List<string> Messages = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add($"[{logLevel}] {formatter(state, exception)} {exception}");
        }
    }
}
