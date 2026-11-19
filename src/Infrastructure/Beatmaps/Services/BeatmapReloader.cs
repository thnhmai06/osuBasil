using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Basil.Domain.Game.Mechanics;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Beatmaps.Services;

internal sealed class BeatmapReloader(
	IBeatmapsetRepository sets,
	IBeatmapRepository beatmaps,
	IBeatmapsetStorage storage,
	IBeatmapAnalyser analyser,
	ILogger<BeatmapReloader> logger) : IBeatmapReloader, IDisposable
{
	private const int PageSize = 100;
	private const int ProgressEvery = 1000;

	private readonly CancellationTokenSource _stop = new();
	private int _running;

	public bool Running => Volatile.Read(ref _running) == 1;

	public bool TryStart()
	{
		if (Interlocked.Exchange(ref _running, 1) == 1) return false;

		_ = Task.Run(() => RunAsync(_stop.Token));
		return true;
	}

	public void Dispose()
	{
		_stop.Cancel();
		_stop.Dispose();
	}

	internal async Task RunAsync(CancellationToken ct)
	{
		try
		{
			var (done, failed) = await ReloadAllAsync(ct);
			logger.LogInformation("Reloaded {Done} beatmaps ({Failed} could not be reloaded)", done, failed);
		}
		catch (OperationCanceledException)
		{
			logger.LogInformation("Reloading beatmaps was stopped");
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Reloading beatmaps failed:");
		}
		finally
		{
			Volatile.Write(ref _running, 0);
		}
	}

	internal async Task<(int Done, int Failed)> ReloadAllAsync(CancellationToken ct)
	{
		int done = 0, failed = 0, offset = 0;
		while (true)
		{
			var page = await sets.ListAsync(new BeatmapQuery(), new PageRequest(offset, PageSize), ct);
			if (page.Items.Count == 0) return (done, failed);

			offset += page.Items.Count;
			foreach (var set in page.Items)
			foreach (var beatmap in await beatmaps.ListAsync(set, ct))
			{
				if (await ReloadAsync(beatmap, ct)) done++;
				else failed++;

				if ((done + failed) % ProgressEvery == 0)
					logger.LogInformation("Reloading beatmaps: {Count} done", done + failed);
			}
		}
	}

	private async Task<bool> ReloadAsync(BeatmapRecord beatmap, CancellationToken ct)
	{
		try
		{
			var stream = await storage.OpenAsync(beatmap.Beatmapset, BeatmapsetFileType.Version, $"{beatmap.Hash}.osu",
				ct);
			if (stream is null) return false;

			BeatmapAnalysis analysis;
			await using (stream)
			{
				analysis = analyser.Analyze(stream, beatmap.Metadata.Difficulty.Mode, GameMods.NoMod);
			}

			beatmap.Metadata = new BeatmapMetadata
			{
				VersionName = beatmap.Metadata.VersionName, Difficulty = analysis.Difficulty,
				ObjectCount = analysis.ObjectCount
			};
			await beatmaps.UpdateAsync(beatmap);
			return true;
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			logger.LogWarning(e, "Beatmap {Id} could not be reloaded and keeps its stored values:", beatmap.Id);
			return false;
		}
	}
}