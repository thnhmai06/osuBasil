using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Rulesets.Messages;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Beatmaps.Services;

internal sealed class BeatmapReloader(
	IBeatmapsetRepository sets,
	IBeatmapRepository beatmaps,
	ISender sender,
	ILogger<BeatmapReloader> logger) : IBeatmapReloader, IDisposable
{
	private const int PageSize = 100;
	private const int ProgressEvery = 1000;

	private readonly CancellationTokenSource _stop = new();
	private int _running;

	public bool Running => Volatile.Read(ref _running) == 1;

	public bool TryStart(bool onlyMissing)
	{
		if (Interlocked.Exchange(ref _running, 1) == 1) return false;

		_ = Task.Run(() => RunAsync(onlyMissing, _stop.Token));
		return true;
	}

	public void Dispose()
	{
		_stop.Cancel();
		_stop.Dispose();
	}

	internal async Task RunAsync(bool onlyMissing, CancellationToken ct)
	{
		try
		{
			var (done, failed) = onlyMissing ? await RequestMissingAsync(ct) : await RequestAllAsync(ct);
			logger.LogInformation("Asked for the difficulty of {Done} beatmaps ({Failed} could not be asked for)", done,
				failed);
		}
		catch (OperationCanceledException)
		{
			logger.LogInformation("Asking for the difficulty of beatmaps was stopped");
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Asking for the difficulty of beatmaps failed:");
		}
		finally
		{
			Volatile.Write(ref _running, 0);
		}
	}

	internal async Task<(int Done, int Failed)> RequestAllAsync(CancellationToken ct)
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
				if (await RequestAsync(beatmap, ct)) done++;
				else failed++;

				if ((done + failed) % ProgressEvery == 0)
					logger.LogInformation("Asking for the difficulty of beatmaps: {Count} done", done + failed);
			}
		}
	}

	internal async Task<(int Done, int Failed)> RequestMissingAsync(CancellationToken ct)
	{
		int done = 0, failed = 0, afterId = 0;
		while (true)
		{
			var batch = await beatmaps.ListUncalculatedAsync(afterId, PageSize, ct);
			if (batch.Count == 0) return (done, failed);

			foreach (var beatmap in batch)
				if (await RequestAsync(beatmap, ct)) done++;
				else failed++;

			afterId = batch[^1].Id;
		}
	}

	// The answer comes as an event; the stored values stay until it does.
	private async Task<bool> RequestAsync(BeatmapRecord beatmap, CancellationToken ct)
	{
		try
		{
			await sender.Send(new CalculateBeatmap(beatmap.Id, beatmap.Hash, beatmap.Metadata.Mode), ct);
			return true;
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			logger.LogWarning(e, "The difficulty of beatmap {Id} could not be asked for:", beatmap.Id);
			return false;
		}
	}
}