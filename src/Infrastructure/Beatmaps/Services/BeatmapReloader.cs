using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Shared;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Beatmaps.Services;

/// <summary>Recomputes the stored metadata of every beatmap from its stored difficulty file, one page of sets at a time.</summary>
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

	/// <inheritdoc />
	public bool Running => Volatile.Read(ref _running) == 1;

	/// <inheritdoc />
	public bool TryStart()
	{
		if (Interlocked.Exchange(ref _running, 1) == 1) return false;

		_ = Task.Run(() => RunAsync(_stop.Token));
		return true;
	}

	/// <summary>Stops a running reload when the server shuts down.</summary>
	public void Dispose()
	{
		_stop.Cancel();
		_stop.Dispose();
	}

	/// <summary>Recomputes every beatmap and ends the running state; a failure is logged, never thrown.</summary>
	/// <param name="ct">A token that stops the reload.</param>
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

	/// <summary>Walks the sets page by page and recomputes the beatmaps of each.</summary>
	/// <returns>How many beatmaps were recomputed and how many were left as they were.</returns>
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
				Version = beatmap.Metadata.Version, Difficulty = analysis.Difficulty, Objects = analysis.Objects
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